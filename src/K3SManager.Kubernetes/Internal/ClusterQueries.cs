using k8s;
using k8s.Autorest;
using k8s.Models;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace K3SManager.Kubernetes.Internal;

/// <summary>
/// The single place that talks to the API server for list operations. Everything above it reads
/// from here so one dashboard render costs one set of list calls, not one per widget.
/// </summary>
internal sealed class ClusterQueries : IDisposable
{
    private const string NodesKey = "nodes";
    private const string PodsKey = "pods";
    private const string NamespacesKey = "namespaces";
    private const string NodeMetricsKey = "node-metrics";

    private readonly IKubernetes _client;
    private readonly ILogger<ClusterQueries> _logger;
    private readonly TimedCache _cache;

    public ClusterQueries(IKubernetes client, IOptions<KubernetesOptions> options, ILogger<ClusterQueries> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _logger = logger;
        _cache = new TimedCache(TimeSpan.FromSeconds(Math.Max(0, options.Value.CacheSeconds)));
    }

    public IKubernetes Client => _client;

    public void Invalidate()
    {
        _cache.Clear();
        _logger.LogDebug("Cluster read cache invalidated after a mutating operation");
    }

    public Task<IReadOnlyList<V1Node>> GetNodesAsync(CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync<IReadOnlyList<V1Node>>(
            NodesKey,
            async ct =>
            {
                var list = await _client.CoreV1.ListNodeAsync(cancellationToken: ct).ConfigureAwait(false);
                return list.Items?.ToList() ?? [];
            },
            cancellationToken);

    public Task<IReadOnlyList<V1Pod>> GetPodsAsync(CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync<IReadOnlyList<V1Pod>>(
            PodsKey,
            async ct =>
            {
                var list = await _client.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: ct).ConfigureAwait(false);
                return list.Items?.ToList() ?? [];
            },
            cancellationToken);

    public Task<IReadOnlyList<V1Namespace>> GetNamespacesAsync(CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync<IReadOnlyList<V1Namespace>>(
            NamespacesKey,
            async ct =>
            {
                var list = await _client.CoreV1.ListNamespaceAsync(cancellationToken: ct).ConfigureAwait(false);
                return list.Items?.ToList() ?? [];
            },
            cancellationToken);

    /// <summary>
    /// Live node usage from metrics-server. A k3s cluster may not have it installed, which is not
    /// an error - the UI simply does not draw the usage bars.
    /// </summary>
    public Task<IReadOnlyDictionary<string, (double Cpu, double Memory)>> GetNodeUsageAsync(CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync<IReadOnlyDictionary<string, (double Cpu, double Memory)>>(
            NodeMetricsKey,
            async ct =>
            {
                var result = new Dictionary<string, (double Cpu, double Memory)>(StringComparer.Ordinal);
                if (_client is not k8s.Kubernetes concrete)
                {
                    return result;
                }

                try
                {
                    // Use the typed custom-object API so cancellation reaches the metrics request.
                    var metrics = await concrete.CustomObjects.ListClusterCustomObjectAsync<NodeMetricsList>(
                        "metrics.k8s.io", "v1beta1", "nodes", cancellationToken: ct).ConfigureAwait(false);
                    foreach (var item in metrics.Items ?? [])
                    {
                        var name = item.Metadata?.Name;
                        if (string.IsNullOrEmpty(name) || item.Usage is null)
                        {
                            continue;
                        }

                        result[name] = (ResourceMath.Cores(item.Usage), ResourceMath.GiB(item.Usage));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "metrics-server is unavailable; node usage will not be displayed");
                }

                return result;
            },
            cancellationToken);

    /// <summary>Translates an API server failure into something an operator can act on.</summary>
    public static KubernetesOperationException Describe(HttpOperationException ex, string operation)
    {
        var status = (int)ex.Response.StatusCode;
        var reason = status switch
        {
            401 => "the API server rejected the credentials in the kubeconfig",
            403 => "the service account is not permitted to perform this action (check the ClusterRole binding)",
            404 => "the object no longer exists",
            409 => "the object was modified by something else - reload and try again",
            422 => "the API server rejected the request body as invalid",
            _ => ex.Response.ReasonPhrase ?? "the API server returned an error"
        };

        return new KubernetesOperationException($"{operation} failed: {reason}.", ex) { StatusCode = status };
    }

    public void Dispose() => _cache.Dispose();
}
