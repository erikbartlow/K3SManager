using K3SManager.Core.Models;

namespace K3SManager.Core.Abstractions;

public interface INamespaceService
{
    Task<IReadOnlyList<NamespaceSummary>> ListAsync(bool includeSystem, CancellationToken cancellationToken);

    Task<NamespaceDetail?> GetDetailAsync(string name, CancellationToken cancellationToken);

    Task CreateAsync(NamespaceCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a namespace and everything in it. Guarded by <see cref="SettingKeys.AllowNamespaceDelete"/>.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken);

    Task ApplyQuotaAsync(string name, NamespaceQuotaRequest request, CancellationToken cancellationToken);
}

public sealed record NamespaceCreateRequest
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Annotations { get; init; } = new Dictionary<string, string>();
}

public sealed record NamespaceQuotaRequest
{
    public string QuotaName { get; init; } = "k3smanager-quota";
    public string? CpuRequests { get; init; }
    public string? CpuLimits { get; init; }
    public string? MemoryRequests { get; init; }
    public string? MemoryLimits { get; init; }
    public int? PodLimit { get; init; }

    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(CpuRequests) ||
        !string.IsNullOrWhiteSpace(CpuLimits) ||
        !string.IsNullOrWhiteSpace(MemoryRequests) ||
        !string.IsNullOrWhiteSpace(MemoryLimits) ||
        PodLimit is > 0;
}
