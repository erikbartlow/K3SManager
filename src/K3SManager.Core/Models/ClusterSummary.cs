namespace K3SManager.Core.Models;

/// <summary>Top-of-dashboard rollup for the whole cluster.</summary>
public sealed record ClusterSummary
{
    public required string DisplayName { get; init; }
    public required string ApiServerUrl { get; init; }

    /// <summary>Most common kubelet version across nodes; nodes that disagree are surfaced separately.</summary>
    public string KubeletVersion { get; init; } = "unknown";
    public bool HasMixedVersions { get; init; }

    public int NodeCount { get; init; }
    public int NodesReady { get; init; }
    public int NodesUnschedulable { get; init; }
    public int ControlPlaneCount { get; init; }

    public int NamespaceCount { get; init; }
    public int PodCount { get; init; }
    public int PodsRunning { get; init; }
    public int PodsPending { get; init; }
    public int PodsFailed { get; init; }

    public double CpuCapacityCores { get; init; }
    public double CpuRequestedCores { get; init; }
    public double MemoryCapacityGiB { get; init; }
    public double MemoryRequestedGiB { get; init; }

    public IReadOnlyList<ClusterEvent> RecentWarnings { get; init; } = [];

    public double CpuCommitPercent => CpuCapacityCores <= 0 ? 0 : Math.Round(CpuRequestedCores / CpuCapacityCores * 100, 1);
    public double MemoryCommitPercent => MemoryCapacityGiB <= 0 ? 0 : Math.Round(MemoryRequestedGiB / MemoryCapacityGiB * 100, 1);
    public bool IsHealthy => NodeCount > 0 && NodesReady == NodeCount && PodsFailed == 0;
}

/// <summary>A flattened Kubernetes event.</summary>
public sealed record ClusterEvent
{
    public required string Namespace { get; init; }
    public required string ObjectKind { get; init; }
    public required string ObjectName { get; init; }
    public required string Reason { get; init; }
    public required string Message { get; init; }
    public string Type { get; init; } = "Normal";
    public int Count { get; init; } = 1;
    public DateTime? LastSeenUtc { get; init; }
}
