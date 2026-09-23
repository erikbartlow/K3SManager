using System.Data;
using CodedThought.Core.Data;

namespace K3SManager.Data.Entities;

/// <summary>
/// Maps the SystemSettings table. The key is a natural string key, not an identity, so inserts and
/// updates are chosen explicitly by the repository rather than by GenericDataStore.Save.
/// </summary>
[DataTable("SystemSettings")]
public sealed class SystemSettingEntity
{
    [DataColumn("SettingKey", DbType.String, 128, DataColumnOptions.PrimaryKey)]
    public string SettingKey { get; set; } = string.Empty;

    [DataColumn("SettingValue", DbType.String, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? SettingValue { get; set; }

    [DataColumn("Description", DbType.String, 512, DataColumnOptions.AllowNull | DataColumnOptions.IsNullableType)]
    public string? Description { get; set; }

    [DataColumn("IsSecret", DbType.Boolean)]
    public bool IsSecret { get; set; }

    [DataColumn("UpdatedUtc", DbType.DateTime)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
