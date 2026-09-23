using System.Data;
using CodedThought.Core.Data;

namespace K3SManager.Data.Entities;

[DataTable("LocalUser")]
public sealed class LocalUserEntity
{
    [DataColumn("UserName", DbType.String, 64, DataColumnOptions.PrimaryKey)]
    public string UserName { get; set; } = string.Empty;
    [DataColumn("PasswordHash", DbType.String, 512)]
    public string PasswordHash { get; set; } = string.Empty;
    [DataColumn("SecurityStamp", DbType.String, 64)]
    public string SecurityStamp { get; set; } = string.Empty;
    [DataColumn("Role", DbType.String, 16)]
    public string Role { get; set; } = K3SManager.Core.Models.UserRoles.ReadOnly;
    [DataColumn("Enabled", DbType.Boolean)]
    public bool Enabled { get; set; }
}
