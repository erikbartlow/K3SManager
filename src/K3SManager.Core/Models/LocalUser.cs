namespace K3SManager.Core.Models;

public sealed class LocalUser
{
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public string Role { get; set; } = UserRoles.ReadOnly;
    public bool Enabled { get; set; } = true;
}
