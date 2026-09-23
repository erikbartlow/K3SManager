using System.Globalization;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Data.Entities;
using K3SManager.Data.Internal;

namespace K3SManager.Data.Repositories;

/// <summary>AuditLog, through CodedThought.Core. Insert-only from the app's point of view.</summary>
internal sealed class AuditRepository : IAuditRepository
{
    private readonly CoreDataStore _data;

    public AuditRepository(CoreDataStore data) => _data = data;

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _data.WriteAsync(
            store => store.SaveNew(new AuditLogEntity
            {
                Action = entry.Action,
                TargetKind = entry.TargetKind,
                TargetName = entry.TargetName,
                TargetNamespace = entry.TargetNamespace,
                Detail = entry.Detail,
                ActorName = entry.ActorName,
                ActorAddress = entry.ActorAddress,
                Succeeded = entry.Succeeded,
                Error = entry.Error,
                CreatedUtc = entry.CreatedUtc
            }),
            cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        // GenericDataStore.GetMultiple has no ordering or row limit, and this table only ever grows,
        // so the newest rows are fetched with an explicit statement. The limit is clamped to an int
        // and formatted invariantly - no caller value reaches the SQL text.
        var limit = Math.Clamp(take, 1, 500).ToString(CultureInfo.InvariantCulture);
        var sql = $"SELECT * FROM AuditLog ORDER BY Id DESC LIMIT {limit};";

        var rows = await _data
            .ReadAsync(store => store.ExecuteNonQueryForList<AuditLogEntity>(sql, null), cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    private static AuditEntry Map(AuditLogEntity entity) => new()
    {
        Id = entity.Id,
        Action = entity.Action,
        TargetKind = entity.TargetKind,
        TargetName = entity.TargetName,
        TargetNamespace = entity.TargetNamespace,
        Detail = entity.Detail,
        ActorName = entity.ActorName,
        ActorAddress = entity.ActorAddress,
        Succeeded = entity.Succeeded,
        Error = entity.Error,
        CreatedUtc = entity.CreatedUtc
    };
}
