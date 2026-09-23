using System.Data;
using CodedThought.Core.Data;
using K3SManager.Core.Models;

namespace K3SManager.Data.Entities;

// Like chorli's NewUserPersistenceRecord: do not mark the supplied key as PrimaryKey
// on the insert shape. SaveNew otherwise omits it as a generated key.
[DataTable("LocalUser")]
public sealed class NewLocalUserPersistenceRecord(LocalUser user)
{
    [DataColumn("UserName", DbType.String, 64, DataColumnOptions.UpdateablePrimaryKey)]
    public string UserName { get; } = user.UserName;
    [DataColumn("PasswordHash", DbType.String, 512)]
    public string PasswordHash { get; } = user.PasswordHash;
    [DataColumn("SecurityStamp", DbType.String, 64)]
    public string SecurityStamp { get; } = user.SecurityStamp;
    [DataColumn("Role", DbType.String, 16)]
    public string Role { get; } = user.Role;
    [DataColumn("Enabled", DbType.Boolean)]
    public bool Enabled { get; } = user.Enabled;
}
