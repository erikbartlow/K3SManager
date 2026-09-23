using K3SManager.Core.Models;

namespace K3SManager.Core.Abstractions;

/// <summary>
/// Key/value configuration held in the database so secrets never live in source or appsettings.
/// </summary>
public interface ISystemSettingsRepository
{
    Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken cancellationToken);

    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken);

    Task SetValueAsync(string key, string? value, CancellationToken cancellationToken);
}

public interface IAuditRepository
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int take, CancellationToken cancellationToken);
}

public interface INamespaceProfileRepository
{
    Task<IReadOnlyDictionary<string, NamespaceProfile>> GetAllAsync(CancellationToken cancellationToken);

    Task<NamespaceProfile?> GetAsync(string namespaceName, CancellationToken cancellationToken);

    Task UpsertAsync(NamespaceProfile profile, CancellationToken cancellationToken);

    Task DeleteAsync(string namespaceName, CancellationToken cancellationToken);
}

/// <summary>Writes an audit row around a mutating call, recording both success and failure.</summary>
public interface IAuditedActionRunner
{
    Task RunAsync(AuditEntry entry, Func<CancellationToken, Task> action, CancellationToken cancellationToken);

    Task<T> RunAsync<T>(AuditEntry entry, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
