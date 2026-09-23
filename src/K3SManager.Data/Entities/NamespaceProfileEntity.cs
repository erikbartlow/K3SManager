using System.Data;
using CodedThought.Core.Data;

namespace K3SManager.Data.Entities;

/// <summary>Maps the NamespaceProfile table - the metadata Kubernetes does not model.</summary>
[DataTable("NamespaceProfile")]
public sealed class NamespaceProfileEntity
{
    [DataColumn("Id", DbType.Int64, DataColumnOptions.PrimaryKey | DataColumnOptions.IsIdentity)]
    public long Id { get; set; }

    [DataColumn("NamespaceName", DbType.String, 253)]
    public string NamespaceName { get; set; } = string.Empty;

    [DataColumn("Owner", DbType.String, 128, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? Owner { get; set; }

    [DataColumn("Description", DbType.String, 512, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? Description { get; set; }

    [DataColumn("Environment", DbType.String, 32, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? Environment { get; set; }

    [DataColumn("IsPinned", DbType.Boolean)]
    public bool IsPinned { get; set; }

    [DataColumn("CreatedUtc", DbType.DateTime)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [DataColumn("UpdatedUtc", DbType.DateTime)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
