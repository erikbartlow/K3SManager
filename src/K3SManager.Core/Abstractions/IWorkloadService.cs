using K3SManager.Core.Models;

namespace K3SManager.Core.Abstractions;

public interface IWorkloadService
{
    Task<IReadOnlyList<WorkloadSummary>> ListAsync(string namespaceName, CancellationToken cancellationToken);

    Task ScaleAsync(string namespaceName, string name, WorkloadKind kind, int replicas, CancellationToken cancellationToken);

    /// <summary>Rolling restart, the same way kubectl rollout restart does it - a pod template annotation bump.</summary>
    Task RestartAsync(string namespaceName, string name, WorkloadKind kind, CancellationToken cancellationToken);

    Task DeletePodAsync(string namespaceName, string podName, CancellationToken cancellationToken);

    Task<string> GetPodLogsAsync(string namespaceName, string podName, string? container, int tailLines, CancellationToken cancellationToken);
}
