using k8s;
using k8s.Autorest;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Core.Options;
using K3SManager.Kubernetes.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace K3SManager.Kubernetes.Services;

internal sealed class ClusterService : IClusterService
{
    private readonly ClusterQueries _queries;
    private readonly ISystemSettingsRepository _settings;
    private readonly KubernetesOptions _options;
    private readonly ILogger<ClusterService> _logger;

    public ClusterService(
        ClusterQueries queries,
        ISystemSettingsRepository settings,
        IOptions<KubernetesOptions> options,
        ILogger<ClusterService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _queries = queries;
        _settings = settings;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ClusterSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var nodesTask = _queries.GetNodesAsync(cancellationToken);
        var podsTask = _queries.GetPodsAsync(cancellationToken);
        var namespacesTask = _queries.GetNamespacesAsync(cancellationToken);
        var warningsTask = GetRecentEventsAsync(8, cancellationToken);
        var displayNameTask = _settings.GetValueAsync(SettingKeys.ClusterDisplayName, cancellationToken);

        await Task.WhenAll(nodesTask, podsTask, namespacesTask, warningsTask, displayNameTask).ConfigureAwait(false);

        var nodes = await nodesTask.ConfigureAwait(false);
        var pods = await podsTask.ConfigureAwait(false);
        var namespaces = await namespacesTask.ConfigureAwait(false);
        var warnings = await warningsTask.ConfigureAwait(false);
        var displayName = await displayNameTask.ConfigureAwait(false);

        var livePods = pods.Where(p => !KubernetesMapper.IsTerminal(p.Status?.Phase)).ToList();
        var requested = livePods
            .Select(p => ResourceMath.SumRequests(p.Spec))
            .Aggregate((Cpu: 0d, Memory: 0d), (acc, r) => (acc.Cpu + r.Cpu, acc.Memory + r.Memory));

        var versions = nodes
            .Select(n => n.Status?.NodeInfo?.KubeletVersion)
            .Where(v => !string.IsNullOrEmpty(v))
            .GroupBy(v => v!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ToList();

        var summary = new ClusterSummary
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "k3s cluster" : displayName,
            ApiServerUrl = _options.ServerOverrideUrl ?? "(from kubeconfig)",
            KubeletVersion = versions.Count > 0 ? versions[0].Key : "unknown",
            HasMixedVersions = versions.Count > 1,
            NodeCount = nodes.Count,
            NodesReady = nodes.Count(n => n.Status?.Conditions?.Any(c =>
                string.Equals(c.Type, "Ready", StringComparison.Ordinal) &&
                string.Equals(c.Status, "True", StringComparison.OrdinalIgnoreCase)) == true),
            NodesUnschedulable = nodes.Count(n => n.Spec?.Unschedulable == true),
            ControlPlaneCount = nodes.Count(n =>
                n.Metadata?.Labels?.ContainsKey("node-role.kubernetes.io/control-plane") == true ||
                n.Metadata?.Labels?.ContainsKey("node-role.kubernetes.io/master") == true),
            NamespaceCount = namespaces.Count,
            PodCount = pods.Count,
            PodsRunning = pods.Count(p => string.Equals(p.Status?.Phase, "Running", StringComparison.Ordinal)),
            PodsPending = pods.Count(p => string.Equals(p.Status?.Phase, "Pending", StringComparison.Ordinal)),
            PodsFailed = pods.Count(p => string.Equals(p.Status?.Phase, "Failed", StringComparison.Ordinal)),
            CpuCapacityCores = Math.Round(nodes.Sum(n => ResourceMath.Cores(n.Status?.Allocatable)), 2),
            CpuRequestedCores = Math.Round(requested.Cpu, 2),
            MemoryCapacityGiB = Math.Round(nodes.Sum(n => ResourceMath.GiB(n.Status?.Allocatable)), 2),
            MemoryRequestedGiB = Math.Round(requested.Memory, 2),
            RecentWarnings = warnings
        };

        _logger.LogDebug(
            "Cluster summary built: {NodeCount} nodes ({NodesReady} ready), {NamespaceCount} namespaces, {PodCount} pods",
            summary.NodeCount,
            summary.NodesReady,
            summary.NamespaceCount,
            summary.PodCount);

        return summary;
    }

    public async Task<IReadOnlyList<ClusterEvent>> GetRecentEventsAsync(int take, CancellationToken cancellationToken)
    {
        try
        {
            var events = await _queries.Client.CoreV1
                .ListEventForAllNamespacesAsync(fieldSelector: "type!=Normal", limit: 200, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return (events.Items ?? [])
                .Select(KubernetesMapper.ToSummary)
                .OrderByDescending(e => e.LastSeenUtc ?? DateTime.MinValue)
                .Take(take)
                .ToList();
        }
        catch (HttpOperationException ex)
        {
            _logger.LogWarning(ex, "Unable to read cluster events; the dashboard will render without them");
            return [];
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _queries.Client.CoreV1
                .ListNamespaceAsync(limit: 1, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API server is unreachable");
            return false;
        }
    }
}
