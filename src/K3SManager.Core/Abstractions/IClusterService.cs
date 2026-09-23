using K3SManager.Core.Models;

namespace K3SManager.Core.Abstractions;

/// <summary>Cluster-wide reads that back the dashboard.</summary>
public interface IClusterService
{
    Task<ClusterSummary> GetSummaryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ClusterEvent>> GetRecentEventsAsync(int take, CancellationToken cancellationToken);

    /// <summary>Round-trips the API server so the UI can show a reachable/unreachable banner.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken);
}
