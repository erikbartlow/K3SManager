using System.Globalization;
using System.Text;
using System.Text.Json;
using k8s;
using k8s.Autorest;
using k8s.Models;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Kubernetes.Internal;
using Microsoft.Extensions.Logging;

namespace K3SManager.Kubernetes.Services;

internal sealed class NodeService : INodeService
{
    private const string DaemonSetKind = "DaemonSet";
    private const string MirrorPodAnnotation = "kubernetes.io/config.mirror";

    private readonly ClusterQueries _queries;
    private readonly ISystemSettingsRepository _settings;
    private readonly ILogger<NodeService> _logger;

    public NodeService(ClusterQueries queries, ISystemSettingsRepository settings, ILogger<NodeService> logger)
    {
        _queries = queries;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NodeSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var nodesTask = _queries.GetNodesAsync(cancellationToken);
        var podsTask = _queries.GetPodsAsync(cancellationToken);
        var usageTask = _queries.GetNodeUsageAsync(cancellationToken);

        await Task.WhenAll(nodesTask, podsTask, usageTask).ConfigureAwait(false);

        var nodes = await nodesTask.ConfigureAwait(false);
        var pods = await podsTask.ConfigureAwait(false);
        var usage = await usageTask.ConfigureAwait(false);

        var podsByNode = pods
            .Where(p => !string.IsNullOrEmpty(p.Spec?.NodeName))
            .GroupBy(p => p.Spec!.NodeName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<V1Pod>)g.ToList(), StringComparer.Ordinal);

        return nodes
            .Select(node =>
            {
                var name = node.Metadata?.Name ?? string.Empty;
                podsByNode.TryGetValue(name, out var nodePods);
                usage.TryGetValue(name, out var nodeUsage);
                return KubernetesMapper.ToSummary(
                    node,
                    nodePods ?? [],
                    usage.ContainsKey(name) ? nodeUsage : null);
            })
            .OrderByDescending(n => n.IsControlPlane)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<NodeSummary?> GetAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var nodes = await ListAsync(cancellationToken).ConfigureAwait(false);
        return nodes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<PodSummary>> GetPodsAsync(string nodeName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);

        var pods = await _queries.GetPodsAsync(cancellationToken).ConfigureAwait(false);

        return pods
            .Where(p => string.Equals(p.Spec?.NodeName, nodeName, StringComparison.Ordinal))
            .Select(KubernetesMapper.ToSummary)
            .OrderBy(p => p.Namespace, StringComparer.Ordinal)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task SetSchedulableAsync(string name, bool schedulable, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var patch = JsonSerializer.Serialize(new
        {
            spec = new { unschedulable = schedulable ? (bool?)null : true }
        });

        await PatchNodeAsync(name, patch, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Node {NodeName} is now {NodeState}",
            name,
            schedulable ? "schedulable (uncordoned)" : "unschedulable (cordoned)");
    }

    public async Task<int> DrainAsync(string name, DrainOptions options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);

        await SetSchedulableAsync(name, schedulable: false, cancellationToken).ConfigureAwait(false);

        var pods = await _queries.GetPodsAsync(cancellationToken).ConfigureAwait(false);
        var candidates = pods
            .Where(p => string.Equals(p.Spec?.NodeName, name, StringComparison.Ordinal))
            .Where(p => !KubernetesMapper.IsTerminal(p.Status?.Phase))
            .Where(p => !IsMirrorPod(p))
            .Where(p => !(options.IgnoreDaemonSets && IsDaemonSetPod(p)))
            .ToList();

        var evicted = 0;
        var failures = new List<string>();

        foreach (var pod in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var podName = pod.Metadata?.Name;
            var podNamespace = pod.Metadata?.NamespaceProperty;
            if (string.IsNullOrEmpty(podName) || string.IsNullOrEmpty(podNamespace))
            {
                continue;
            }

            var eviction = new V1Eviction
            {
                Metadata = new V1ObjectMeta { Name = podName, NamespaceProperty = podNamespace },
                DeleteOptions = new V1DeleteOptions { GracePeriodSeconds = options.GracePeriodSeconds }
            };

            try
            {
                await _queries.Client.CoreV1
                    .CreateNamespacedPodEvictionAsync(eviction, podName, podNamespace, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                evicted++;
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // A PodDisruptionBudget is holding this pod back. That is the budget doing its job.
                failures.Add($"{podNamespace}/{podName} (blocked by a PodDisruptionBudget)");
                _logger.LogWarning(
                    "Eviction of {Namespace}/{PodName} was refused by a PodDisruptionBudget during drain of {NodeName}",
                    podNamespace,
                    podName,
                    name);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                evicted++;
            }
        }

        _queries.Invalidate();

        _logger.LogWarning(
            "Drain of node {NodeName} evicted {EvictedCount} of {CandidateCount} pods ({FailureCount} refused)",
            name,
            evicted,
            candidates.Count,
            failures.Count);

        if (failures.Count > 0 && !options.Force)
        {
            throw new KubernetesOperationException(
                $"Drained {evicted} pod(s). These could not be evicted: {string.Join("; ", failures)}.");
        }

        return evicted;
    }

    public async Task SetLabelAsync(string name, string key, string? value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var labels = new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value };
        var patch = JsonSerializer.Serialize(new { metadata = new { labels } });

        await PatchNodeAsync(name, patch, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Label {LabelKey} on node {NodeName} was {LabelAction}",
            key,
            name,
            value is null ? "removed" : "set");
    }

    public async Task AddTaintAsync(string name, NodeTaint taint, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(taint);

        var node = await ReadNodeAsync(name, cancellationToken).ConfigureAwait(false);
        var taints = node.Spec?.Taints?.ToList() ?? [];

        if (taints.Any(t => string.Equals(t.Key, taint.Key, StringComparison.Ordinal) &&
                            string.Equals(t.Effect, taint.Effect, StringComparison.Ordinal)))
        {
            throw new KubernetesOperationException($"Node '{name}' already carries the taint {taint}.");
        }

        taints.Add(new V1Taint { Key = taint.Key, Value = taint.Value, Effect = taint.Effect });
        await ReplaceTaintsAsync(name, node, taints, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("Taint {Taint} added to node {NodeName}", taint.ToString(), name);
    }

    public async Task RemoveTaintAsync(string name, string key, string effect, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var node = await ReadNodeAsync(name, cancellationToken).ConfigureAwait(false);
        var taints = (node.Spec?.Taints ?? [])
            .Where(t => !(string.Equals(t.Key, key, StringComparison.Ordinal) &&
                          string.Equals(t.Effect, effect, StringComparison.Ordinal)))
            .ToList();

        await ReplaceTaintsAsync(name, node, taints, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("Taint {TaintKey}:{TaintEffect} removed from node {NodeName}", key, effect, name);
    }

    public async Task RemoveAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var node = await ReadNodeAsync(name, cancellationToken).ConfigureAwait(false);
        var isControlPlane =
            node.Metadata?.Labels?.ContainsKey("node-role.kubernetes.io/control-plane") == true ||
            node.Metadata?.Labels?.ContainsKey("node-role.kubernetes.io/master") == true;

        if (isControlPlane)
        {
            throw new KubernetesOperationException(
                $"'{name}' is a control-plane node. Removing it here would leave the cluster without a server - do that from the host.");
        }

        try
        {
            await _queries.Client.CoreV1.DeleteNodeAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
            _queries.Invalidate();
            _logger.LogWarning("Node {NodeName} removed from the cluster registry", name);
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Removing node '{name}'");
        }
    }

    public async Task<JoinInstruction> BuildJoinInstructionAsync(JoinRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var serverUrlTask = _settings.GetValueAsync(SettingKeys.K3sServerUrl, cancellationToken);
        var tokenTask = _settings.GetValueAsync(SettingKeys.K3sNodeToken, cancellationToken);
        var channelTask = _settings.GetValueAsync(SettingKeys.K3sInstallChannel, cancellationToken);

        await Task.WhenAll(serverUrlTask, tokenTask, channelTask).ConfigureAwait(false);

        var serverUrl = await serverUrlTask.ConfigureAwait(false);
        var token = await tokenTask.ConfigureAwait(false);
        var channel = await channelTask.ConfigureAwait(false);

        var hasServer = !string.IsNullOrWhiteSpace(serverUrl);
        var hasToken = !string.IsNullOrWhiteSpace(token);

        var builder = new StringBuilder();
        builder.Append("curl -sfL https://get.k3s.io | ");

        if (!string.IsNullOrWhiteSpace(channel))
        {
            builder.Append(CultureInfo.InvariantCulture, $"INSTALL_K3S_CHANNEL={channel} ");
        }

        builder.Append(CultureInfo.InvariantCulture, $"K3S_URL={(hasServer ? serverUrl : "https://<server>:6443")} ");
        builder.Append(CultureInfo.InvariantCulture, $"K3S_TOKEN={(hasToken ? token : "<node-token>")} ");

        var agentArgs = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.NodeName))
        {
            agentArgs.Add($"--node-name {request.NodeName.Trim()}");
        }

        agentArgs.AddRange(request.Labels
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => $"--node-label {l.Trim()}"));

        agentArgs.AddRange(request.Taints
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => $"--node-taint {t.Trim()}"));

        if (agentArgs.Count > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"INSTALL_K3S_EXEC=\"agent {string.Join(' ', agentArgs)}\" ");
        }

        builder.Append("sh -");

        var warning = (hasServer, hasToken) switch
        {
            (false, false) => "The k3s server URL and node token are not configured. Set them in Settings, or fill in the placeholders by hand.",
            (false, true) => "The k3s server URL is not configured. Set it in Settings, or replace the placeholder by hand.",
            (true, false) => "The k3s node token is not configured. It lives at /var/lib/rancher/k3s/server/node-token on the server; store it in Settings.",
            _ => null
        };

        return new JoinInstruction
        {
            ServerUrl = hasServer ? serverUrl! : "https://<server>:6443",
            Command = builder.ToString(),
            NodeName = request.NodeName,
            Labels = request.Labels,
            Taints = request.Taints,
            IsComplete = hasServer && hasToken,
            Warning = warning
        };
    }

    private async Task<V1Node> ReadNodeAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await _queries.Client.CoreV1.ReadNodeAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new KubernetesOperationException($"Node '{name}' is not registered with this cluster.", ex) { StatusCode = 404 };
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Reading node '{name}'");
        }
    }

    private async Task ReplaceTaintsAsync(string name, V1Node node, IList<V1Taint> taints, CancellationToken cancellationToken)
    {
        node.Spec ??= new V1NodeSpec();
        node.Spec.Taints = taints;

        try
        {
            await _queries.Client.CoreV1.ReplaceNodeAsync(node, name, cancellationToken: cancellationToken).ConfigureAwait(false);
            _queries.Invalidate();
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Updating taints on node '{name}'");
        }
    }

    private async Task PatchNodeAsync(string name, string patchJson, CancellationToken cancellationToken)
    {
        try
        {
            var patch = new V1Patch(patchJson, V1Patch.PatchType.MergePatch);
            await _queries.Client.CoreV1.PatchNodeAsync(patch, name, cancellationToken: cancellationToken).ConfigureAwait(false);
            _queries.Invalidate();
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Updating node '{name}'");
        }
    }

    private static bool IsDaemonSetPod(V1Pod pod) =>
        pod.Metadata?.OwnerReferences?.Any(o => string.Equals(o.Kind, DaemonSetKind, StringComparison.Ordinal)) == true;

    private static bool IsMirrorPod(V1Pod pod) =>
        pod.Metadata?.Annotations?.ContainsKey(MirrorPodAnnotation) == true;
}
