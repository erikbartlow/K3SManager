namespace K3SManager.Core.Models;

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string ReadOnly = "ReadOnly";
    public static bool IsValid(string? role) => role is Admin or ReadOnly;
}
