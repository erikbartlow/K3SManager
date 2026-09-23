using K3SManager.Core.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace K3SManager.Web.Infrastructure;

/// <summary>Reports the API server as the app's one hard dependency.</summary>
internal sealed class ClusterHealthCheck : IHealthCheck
{
    private readonly IClusterService _cluster;

    public ClusterHealthCheck(IClusterService cluster) => _cluster = cluster;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var reachable = await _cluster.PingAsync(cancellationToken).ConfigureAwait(false);

        return reachable
            ? HealthCheckResult.Healthy("API server reachable")
            : HealthCheckResult.Unhealthy("API server unreachable");
    }
}
