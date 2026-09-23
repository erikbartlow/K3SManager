namespace K3SManager.Core.Models;

public enum WorkloadKind
{
    Deployment,
    StatefulSet,
    DaemonSet,
    CronJob,
    Job
}

/// <summary>A controller-style workload inside a namespace.</summary>
public sealed record WorkloadSummary
{
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public WorkloadKind Kind { get; init; }
    public int DesiredReplicas { get; init; }
    public int ReadyReplicas { get; init; }
    public int UpdatedReplicas { get; init; }
    public int AvailableReplicas { get; init; }
    public string Images { get; init; } = string.Empty;
    public DateTime? CreatedUtc { get; init; }

    public bool IsHealthy => ReadyReplicas >= DesiredReplicas;
    public bool CanScale => Kind is WorkloadKind.Deployment or WorkloadKind.StatefulSet;
    public string ReplicaLabel => $"{ReadyReplicas}/{DesiredReplicas}";
}

/// <summary>A pod, flattened for the tables that list them.</summary>
public sealed record PodSummary
{
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public string Phase { get; init; } = "Unknown";
    public string? NodeName { get; init; }
    public string? PodIp { get; init; }
    public int RestartCount { get; init; }
    public int ContainersReady { get; init; }
    public int ContainerCount { get; init; }
    public DateTime? StartedUtc { get; init; }
    public double CpuRequestedCores { get; init; }
    public double MemoryRequestedGiB { get; init; }
    public string? OwnerKind { get; init; }
    public string? OwnerName { get; init; }

    public bool IsReady => ContainerCount > 0 && ContainersReady == ContainerCount;
    public bool IsTerminal => Phase is "Succeeded" or "Failed";
    public string ContainerLabel => $"{ContainersReady}/{ContainerCount}";
    public TimeSpan? Age => StartedUtc is null ? null : DateTime.UtcNow - StartedUtc.Value;
}

/// <summary>A namespaced service, for the namespace detail page.</summary>
public sealed record ServiceSummary
{
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public string Type { get; init; } = "ClusterIP";
    public string? ClusterIp { get; init; }
    public string Ports { get; init; } = string.Empty;
    public DateTime? CreatedUtc { get; init; }
}

/// <summary>Namespace detail payload - one round trip, everything the detail page renders.</summary>
public sealed record NamespaceDetail
{
    public required NamespaceSummary Summary { get; init; }
    public IReadOnlyList<WorkloadSummary> Workloads { get; init; } = [];
    public IReadOnlyList<PodSummary> Pods { get; init; } = [];
    public IReadOnlyList<ServiceSummary> Services { get; init; } = [];
    public IReadOnlyList<ClusterEvent> Events { get; init; } = [];
    public IReadOnlyList<ResourceQuotaSummary> Quotas { get; init; } = [];
}

public sealed record ResourceQuotaSummary
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, string> Hard { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Used { get; init; } = new Dictionary<string, string>();
}
