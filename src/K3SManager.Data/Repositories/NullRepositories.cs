using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace K3SManager.Data.Repositories;

/// <summary>
/// Used when Data:Enabled is false. The app then runs as a read-only cluster viewer: no settings,
/// no audit trail, no namespace metadata - but every page still renders.
/// </summary>
internal sealed class NullSystemSettingsRepository : ISystemSettingsRepository
{
    public Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SystemSetting>>([]);

    public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public Task SetValueAsync(string key, string? value, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Persistence is disabled (Data:Enabled = false), so settings cannot be saved.");
}

internal sealed class NullAuditRepository : IAuditRepository
{
    private readonly ILogger<NullAuditRepository> _logger;

    public NullAuditRepository(ILogger<NullAuditRepository> logger) => _logger = logger;

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Persistence is off, so the log is the only audit trail there is.
        _logger.LogInformation(
            "AUDIT {Action} {TargetKind} {TargetNamespace}/{TargetName} by {Actor} succeeded={Succeeded}",
            entry.Action,
            entry.TargetKind,
            entry.TargetNamespace ?? "-",
            entry.TargetName,
            entry.ActorName,
            entry.Succeeded);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>([]);
}

internal sealed class NullNamespaceProfileRepository : INamespaceProfileRepository
{
    public Task<IReadOnlyDictionary<string, NamespaceProfile>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, NamespaceProfile>>(
            new Dictionary<string, NamespaceProfile>(StringComparer.Ordinal));

    public Task<NamespaceProfile?> GetAsync(string namespaceName, CancellationToken cancellationToken) =>
        Task.FromResult<NamespaceProfile?>(null);

    public Task UpsertAsync(NamespaceProfile profile, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Persistence is disabled (Data:Enabled = false), so namespace metadata cannot be saved.");

    public Task DeleteAsync(string namespaceName, CancellationToken cancellationToken) => Task.CompletedTask;
}
