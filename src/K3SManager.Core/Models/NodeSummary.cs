namespace K3SManager.Core.Models;

/// <summary>A cluster node, flattened for the node list and detail pages.</summary>
public sealed record NodeSummary
{
    public required string Name { get; init; }
    public bool IsReady { get; init; }
    public bool IsControlPlane { get; init; }
    public bool IsUnschedulable { get; init; }
    public string Status => !IsReady ? "NotReady" : IsUnschedulable ? "Ready,SchedulingDisabled" : "Ready";

    public string Roles { get; init; } = "worker";
    public string KubeletVersion { get; init; } = "unknown";
    public string ContainerRuntime { get; init; } = string.Empty;
    public string OsImage { get; init; } = string.Empty;
    public string KernelVersion { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string? InternalIp { get; init; }
    public DateTime? CreatedUtc { get; init; }

    public double CpuCapacityCores { get; init; }
    public double CpuAllocatableCores { get; init; }
    public double CpuRequestedCores { get; init; }
    public double MemoryCapacityGiB { get; init; }
    public double MemoryAllocatableGiB { get; init; }
    public double MemoryRequestedGiB { get; init; }
    public int PodCapacity { get; init; }
    public int PodCount { get; init; }

    /// <summary>Live usage from metrics-server; null when metrics-server is not installed.</summary>
    public double? CpuUsageCores { get; init; }
    public double? MemoryUsageGiB { get; init; }

    public IReadOnlyList<NodeCondition> Conditions { get; init; } = [];
    public IReadOnlyList<NodeTaint> Taints { get; init; } = [];
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    public TimeSpan? Age => CreatedUtc is null ? null : DateTime.UtcNow - CreatedUtc.Value;
    public double CpuCommitPercent => CpuAllocatableCores <= 0 ? 0 : Math.Round(CpuRequestedCores / CpuAllocatableCores * 100, 1);
    public double MemoryCommitPercent => MemoryAllocatableGiB <= 0 ? 0 : Math.Round(MemoryRequestedGiB / MemoryAllocatableGiB * 100, 1);
    public IEnumerable<NodeCondition> Pressures => Conditions.Where(c => c.IsPressure && c.IsActive);
}

public sealed record NodeCondition
{
    public required string Type { get; init; }
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public DateTime? LastTransitionUtc { get; init; }

    public bool IsPressure => Type is "MemoryPressure" or "DiskPressure" or "PIDPressure" or "NetworkUnavailable";
    public bool IsActive => string.Equals(Status, "True", StringComparison.OrdinalIgnoreCase);
}

public sealed record NodeTaint
{
    public required string Key { get; init; }
    public string? Value { get; init; }
    public required string Effect { get; init; }

    public override string ToString() => string.IsNullOrEmpty(Value) ? $"{Key}:{Effect}" : $"{Key}={Value}:{Effect}";
}
