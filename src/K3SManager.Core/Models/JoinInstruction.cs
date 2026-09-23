namespace K3SManager.Core.Models;

/// <summary>
/// Kubernetes has no API for adding a machine to a cluster - a node registers itself when the
/// k3s agent starts. This is the rendered instruction set the Nodes/Join page hands the operator.
/// </summary>
public sealed record JoinInstruction
{
    public required string ServerUrl { get; init; }
    public required string Command { get; init; }
    public string? NodeName { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<string> Taints { get; init; } = [];

    /// <summary>True when the node token is present, so the command is runnable as rendered.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Set when the command could not be fully rendered - shown to the operator instead.</summary>
    public string? Warning { get; init; }
}
