namespace K3SManager.Core.Models;

/// <summary>Every mutating action the app performs is written here before it is attempted.</summary>
public sealed record AuditEntry
{
    public long Id { get; init; }
    public required string Action { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetName { get; init; }
    public string? TargetNamespace { get; init; }
    public string? Detail { get; init; }
    public required string ActorName { get; init; }
    public string? ActorAddress { get; init; }
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>A key/value row from the SystemSettings table.</summary>
public sealed record SystemSetting
{
    public required string SettingKey { get; init; }
    public string? SettingValue { get; init; }
    public string? Description { get; init; }

    /// <summary>Secrets are masked in the UI and never echoed back into a form field.</summary>
    public bool IsSecret { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

/// <summary>Well-known SystemSettings keys. Values live in the database, never in source.</summary>
public static class SettingKeys
{
    public const string ClusterDisplayName = "Cluster.DisplayName";
    public const string K3sServerUrl = "K3s.ServerUrl";
    public const string K3sNodeToken = "K3s.NodeToken";
    public const string K3sInstallChannel = "K3s.InstallChannel";
    public const string UiRefreshSeconds = "Ui.RefreshSeconds";
    public const string AllowNamespaceDelete = "Safety.AllowNamespaceDelete";
    public const string AllowNodeDrain = "Safety.AllowNodeDrain";
}
