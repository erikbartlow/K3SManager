using CodedThought.Core.Data;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Data.Entities;
using K3SManager.Data.Internal;

namespace K3SManager.Data.Repositories;

/// <summary>
/// NamespaceProfile, through CodedThought.Core. The table has an identity key but the app addresses
/// rows by namespace name, so an upsert reads the row first and then saves it as new or existing.
/// </summary>
internal sealed class NamespaceProfileRepository : INamespaceProfileRepository
{
    private readonly CoreDataStore _data;

    public NamespaceProfileRepository(CoreDataStore data) => _data = data;

    public async Task<IReadOnlyDictionary<string, NamespaceProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var rows = await _data
            .ReadAsync(store => store.GetMultiple<NamespaceProfileEntity>(null), cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<string, NamespaceProfile>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            result[row.NamespaceName] = Map(row);
        }

        return result;
    }

    public async Task<NamespaceProfile?> GetAsync(string namespaceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        var row = await _data
            .ReadAsync(store => FindByName(store, namespaceName), cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Map(row);
    }

    public Task UpsertAsync(NamespaceProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.NamespaceName);

        return _data.WriteAsync(
            store =>
            {
                var now = DateTime.UtcNow;
                var existing = FindByName(store, profile.NamespaceName);

                if (existing is null)
                {
                    store.SaveNew(new NamespaceProfileEntity
                    {
                        NamespaceName = profile.NamespaceName,
                        Owner = profile.Owner,
                        Description = profile.Description,
                        Environment = profile.Environment,
                        IsPinned = profile.IsPinned,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    });
                    return;
                }

                existing.Owner = profile.Owner;
                existing.Description = profile.Description;
                existing.Environment = profile.Environment;
                existing.IsPinned = profile.IsPinned;
                existing.UpdatedUtc = now;
                store.SaveExisting(existing);
            },
            cancellationToken);
    }

    public Task DeleteAsync(string namespaceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        return _data.WriteAsync(
            store =>
            {
                ParameterCollection parameters = store.CreateParameterCollection();
                parameters.AddStringParameter("NamespaceName", namespaceName);
                store.Remove<NamespaceProfileEntity>(parameters);
            },
            cancellationToken);
    }

    private static NamespaceProfileEntity? FindByName(GenericDataStore store, string namespaceName)
    {
        ParameterCollection parameters = store.CreateParameterCollection();
        parameters.AddStringParameter("NamespaceName", namespaceName);
        return store.GetMultiple<NamespaceProfileEntity>(parameters).FirstOrDefault();
    }

    private static NamespaceProfile Map(NamespaceProfileEntity entity) => new()
    {
        Id = entity.Id,
        NamespaceName = entity.NamespaceName,
        Owner = entity.Owner,
        Description = entity.Description,
        Environment = entity.Environment,
        IsPinned = entity.IsPinned,
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc
    };
}
