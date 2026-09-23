using System.Globalization;
using System.Text.Json;
using k8s;
using k8s.Autorest;
using k8s.Models;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Kubernetes.Internal;
using Microsoft.Extensions.Logging;

namespace K3SManager.Kubernetes.Services;

internal sealed class WorkloadService : IWorkloadService
{
    private const string RestartAnnotation = "kubectl.kubernetes.io/restartedAt";
    private const int MaxLogBytes = 512 * 1024;

    private readonly ClusterQueries _queries;
    private readonly ILogger<WorkloadService> _logger;

    public WorkloadService(ClusterQueries queries, ILogger<WorkloadService> logger)
    {
        _queries = queries;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkloadSummary>> ListAsync(string namespaceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        var client = _queries.Client;

        try
        {
            var deploymentsTask = client.AppsV1.ListNamespacedDeploymentAsync(namespaceName, cancellationToken: cancellationToken);
            var statefulSetsTask = client.AppsV1.ListNamespacedStatefulSetAsync(namespaceName, cancellationToken: cancellationToken);
            var daemonSetsTask = client.AppsV1.ListNamespacedDaemonSetAsync(namespaceName, cancellationToken: cancellationToken);

            await Task.WhenAll(deploymentsTask, statefulSetsTask, daemonSetsTask).ConfigureAwait(false);

            var deployments = (await deploymentsTask.ConfigureAwait(false)).Items ?? [];
            var statefulSets = (await statefulSetsTask.ConfigureAwait(false)).Items ?? [];
            var daemonSets = (await daemonSetsTask.ConfigureAwait(false)).Items ?? [];

            return deployments.Select(KubernetesMapper.ToSummary)
                .Concat(statefulSets.Select(KubernetesMapper.ToSummary))
                .Concat(daemonSets.Select(KubernetesMapper.ToSummary))
                .OrderBy(w => w.Kind)
                .ThenBy(w => w.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Listing workloads in '{namespaceName}'");
        }
    }

    public async Task ScaleAsync(
        string namespaceName,
        string name,
        WorkloadKind kind,
        int replicas,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(replicas);

        if (kind is not (WorkloadKind.Deployment or WorkloadKind.StatefulSet))
        {
            throw new KubernetesOperationException($"A {kind} has no replica count to scale.");
        }

        var patch = new V1Patch(
            JsonSerializer.Serialize(new { spec = new { replicas } }),
            V1Patch.PatchType.MergePatch);

        try
        {
            if (kind == WorkloadKind.Deployment)
            {
                await _queries.Client.AppsV1
                    .PatchNamespacedDeploymentScaleAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _queries.Client.AppsV1
                    .PatchNamespacedStatefulSetScaleAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _queries.Invalidate();

            _logger.LogWarning(
                "{Kind} {Namespace}/{Name} scaled to {Replicas} replicas",
                kind,
                namespaceName,
                name,
                replicas);
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Scaling {kind.ToString().ToLowerInvariant()} '{name}'");
        }
    }

    public async Task RestartAsync(string namespaceName, string name, WorkloadKind kind, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var stamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var body = JsonSerializer.Serialize(new
        {
            spec = new
            {
                template = new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string>(StringComparer.Ordinal) { [RestartAnnotation] = stamp }
                    }
                }
            }
        });

        var patch = new V1Patch(body, V1Patch.PatchType.StrategicMergePatch);

        try
        {
            switch (kind)
            {
                case WorkloadKind.Deployment:
                    await _queries.Client.AppsV1
                        .PatchNamespacedDeploymentAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case WorkloadKind.StatefulSet:
                    await _queries.Client.AppsV1
                        .PatchNamespacedStatefulSetAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case WorkloadKind.DaemonSet:
                    await _queries.Client.AppsV1
                        .PatchNamespacedDaemonSetAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new KubernetesOperationException($"A {kind} cannot be restarted this way.");
            }

            _queries.Invalidate();
            _logger.LogWarning("Rolling restart requested for {Kind} {Namespace}/{Name}", kind, namespaceName, name);
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Restarting {kind.ToString().ToLowerInvariant()} '{name}'");
        }
    }

    public async Task DeletePodAsync(string namespaceName, string podName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(podName);

        try
        {
            await _queries.Client.CoreV1
                .DeleteNamespacedPodAsync(podName, namespaceName, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _queries.Invalidate();
            _logger.LogWarning("Pod {Namespace}/{PodName} deleted", namespaceName, podName);
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Deleting pod '{podName}'");
        }
    }

    public async Task<string> GetPodLogsAsync(
        string namespaceName,
        string podName,
        string? container,
        int tailLines,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(podName);

        var tail = Math.Clamp(tailLines, 10, 5000);

        try
        {
            await using var stream = await _queries.Client.CoreV1
                .ReadNamespacedPodLogAsync(
                    podName,
                    namespaceName,
                    container: string.IsNullOrWhiteSpace(container) ? null : container,
                    tailLines: tail,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            using var reader = new StreamReader(stream);
            var buffer = new char[MaxLogBytes];
            var read = await reader.ReadBlockAsync(buffer, cancellationToken).ConfigureAwait(false);
            return new string(buffer, 0, read);
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Reading logs for pod '{podName}'");
        }
    }
}
