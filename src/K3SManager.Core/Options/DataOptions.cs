namespace K3SManager.Core.Options;

/// <summary>Binding target for the "Data" configuration section.</summary>
public sealed class DataOptions
{
    public const string SectionName = "Data";

    /// <summary>
    /// Name selected by CodedThought.Core from CoreSettings:Connections in appsettings.json.
    /// Leave empty to use the framework's primary connection. In the cluster, the settings
    /// file is mounted from the node into the application container.
    /// </summary>
    public string ConnectionName { get; init; } = string.Empty;

    /// <summary>When false the app runs read-only against the cluster with no persistence.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Command timeout, in seconds, for repository calls. 0 uses the provider default.</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;
}
