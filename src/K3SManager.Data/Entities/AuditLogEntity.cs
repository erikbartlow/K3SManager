using System.Data;
using CodedThought.Core.Data;

namespace K3SManager.Data.Entities;

/// <summary>Maps the AuditLog table. Insert-only from the app's point of view.</summary>
[DataTable("AuditLog")]
public sealed class AuditLogEntity
{
    [DataColumn("Id", DbType.Int64, DataColumnOptions.PrimaryKey | DataColumnOptions.IsIdentity)]
    public long Id { get; set; }

    [DataColumn("Action", DbType.String, 64)]
    public string Action { get; set; } = string.Empty;

    [DataColumn("TargetKind", DbType.String, 64)]
    public string TargetKind { get; set; } = string.Empty;

    [DataColumn("TargetName", DbType.String, 253)]
    public string TargetName { get; set; } = string.Empty;

    [DataColumn("TargetNamespace", DbType.String, 253, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? TargetNamespace { get; set; }

    [DataColumn("Detail", DbType.String, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? Detail { get; set; }

    [DataColumn("ActorName", DbType.String, 128)]
    public string ActorName { get; set; } = string.Empty;

    [DataColumn("ActorAddress", DbType.String, 64, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? ActorAddress { get; set; }

    [DataColumn("Succeeded", DbType.Boolean)]
    public bool Succeeded { get; set; }

    [DataColumn("Error", DbType.String, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? Error { get; set; }

    [DataColumn("CreatedUtc", DbType.DateTime)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
