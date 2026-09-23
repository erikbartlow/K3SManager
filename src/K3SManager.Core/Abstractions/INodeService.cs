using K3SManager.Core.Models;

namespace K3SManager.Core.Abstractions;

public interface INodeService
{
    Task<IReadOnlyList<NodeSummary>> ListAsync(CancellationToken cancellationToken);

    Task<NodeSummary?> GetAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<PodSummary>> GetPodsAsync(string nodeName, CancellationToken cancellationToken);

    /// <summary>Marks the node unschedulable (cordon) or schedulable again (uncordon).</summary>
    Task SetSchedulableAsync(string name, bool schedulable, CancellationToken cancellationToken);

    /// <summary>
    /// Cordons the node, then evicts every pod on it that is not a DaemonSet or mirror pod.
    /// Returns the number of pods evicted.
    /// </summary>
    Task<int> DrainAsync(string name, DrainOptions options, CancellationToken cancellationToken);

    Task SetLabelAsync(string name, string key, string? value, CancellationToken cancellationToken);

    Task AddTaintAsync(string name, NodeTaint taint, CancellationToken cancellationToken);

    Task RemoveTaintAsync(string name, string key, string effect, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the node object from the cluster. The machine itself must be torn down separately
    /// with k3s-agent-uninstall.sh - the UI says so before it asks for confirmation.
    /// </summary>
    Task RemoveAsync(string name, CancellationToken cancellationToken);

    /// <summary>Renders the k3s agent join command for a new worker.</summary>
    Task<JoinInstruction> BuildJoinInstructionAsync(JoinRequest request, CancellationToken cancellationToken);
}

public sealed record DrainOptions
{
    public bool IgnoreDaemonSets { get; init; } = true;
    public bool DeleteEmptyDirData { get; init; }
    public bool Force { get; init; }
    public int GracePeriodSeconds { get; init; } = 30;
}

public sealed record JoinRequest
{
    public string? NodeName { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<string> Taints { get; init; } = [];
}
