namespace K3SManager.Web.Infrastructure;

/// <summary>
/// Whether the MySQL side of the app is wired up. Resolved once at startup so views and
/// controllers can hide the pieces that need a database without probing for it.
/// </summary>
public sealed class PersistenceState
{
    public PersistenceState(bool enabled) => Enabled = enabled;

    public bool Enabled { get; }
}
