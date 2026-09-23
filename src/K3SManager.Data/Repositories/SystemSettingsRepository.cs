using CodedThought.Core.Data;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Data.Entities;
using K3SManager.Data.Internal;
using Microsoft.Extensions.Logging;

namespace K3SManager.Data.Repositories;

/// <summary>
/// SystemSettings, through CodedThought.Core. The key is a natural string key, so
/// <c>GenericDataStore.Save</c> would always resolve it as an update - inserts and updates are
/// chosen here instead, from whether the row already exists.
/// </summary>
internal sealed class SystemSettingsRepository : ISystemSettingsRepository
{
    private readonly CoreDataStore _data;
    private readonly ILogger<SystemSettingsRepository> _logger;

    public SystemSettingsRepository(CoreDataStore data, ILogger<SystemSettingsRepository> logger)
    {
        _data = data;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken cancellationToken)
    {
        var rows = await _data
            .ReadAsync(store => store.GetMultiple<SystemSettingEntity>(null), cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(Map)
            .OrderBy(s => s.SettingKey, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var row = await _data
            .ReadAsync(store => FindByKey(store, key), cancellationToken)
            .ConfigureAwait(false);

        return row?.SettingValue;
    }

    public async Task SetValueAsync(string key, string? value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _data.WriteAsync(
            store =>
            {
                var existing = FindByKey(store, key);

                if (existing is null)
                {
                    store.SaveNew(new SystemSettingEntity
                    {
                        SettingKey = key,
                        SettingValue = value,
                        UpdatedUtc = DateTime.UtcNow
                    });
                    return;
                }

                existing.SettingValue = value;
                existing.UpdatedUtc = DateTime.UtcNow;
                store.SaveExisting(existing);
            },
            cancellationToken).ConfigureAwait(false);

        // The value itself is never logged - one of these rows holds the cluster node token.
        _logger.LogInformation("System setting {SettingKey} updated", key);
    }

    private static SystemSettingEntity? FindByKey(GenericDataStore store, string key)
    {
        ParameterCollection parameters = store.CreateParameterCollection();
        parameters.AddStringParameter("SettingKey", key);
        return store.GetMultiple<SystemSettingEntity>(parameters).FirstOrDefault();
    }

    private static SystemSetting Map(SystemSettingEntity entity) => new()
    {
        SettingKey = entity.SettingKey,
        SettingValue = entity.SettingValue,
        Description = entity.Description,
        IsSecret = entity.IsSecret,
        UpdatedUtc = entity.UpdatedUtc
    };
}
