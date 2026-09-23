namespace K3SManager.Core.Models;

/// <summary>A namespace as the namespace list renders it.</summary>
public sealed record NamespaceSummary
{
    public required string Name { get; init; }
    public string Phase { get; init; } = "Unknown";
    public DateTime? CreatedUtc { get; init; }
    public bool IsSystem { get; init; }

    public int PodCount { get; init; }
    public int PodsRunning { get; init; }
    public int PodsNotReady { get; init; }
    public int DeploymentCount { get; init; }
    public int StatefulSetCount { get; init; }
    public int DaemonSetCount { get; init; }
    public int ServiceCount { get; init; }

    public double CpuRequestedCores { get; init; }
    public double MemoryRequestedGiB { get; init; }

    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>Operator-supplied metadata held in the app database, not in the cluster.</summary>
    public NamespaceProfile? Profile { get; init; }

    public TimeSpan? Age => CreatedUtc is null ? null : DateTime.UtcNow - CreatedUtc.Value;
    public bool HasProblems => PodsNotReady > 0 || !string.Equals(Phase, "Active", StringComparison.Ordinal);
}

/// <summary>Everything the app stores about a namespace that Kubernetes does not model.</summary>
public sealed record NamespaceProfile
{
    public long Id { get; init; }
    public required string NamespaceName { get; init; }
    public string? Owner { get; init; }
    public string? Description { get; init; }
    public string? Environment { get; init; }
    public bool IsPinned { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
}
