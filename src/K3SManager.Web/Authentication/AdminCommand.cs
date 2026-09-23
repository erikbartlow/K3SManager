using System.Text;
using System.Text.RegularExpressions;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using Microsoft.AspNetCore.Identity;

namespace K3SManager.Web.Authentication;

public static class AdminCommand
{
    public static async Task RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Run --create-admin in an interactive terminal. Password input must not be redirected.");
        Console.Write("Administrator username: ");
        var name = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(name, "^[A-Z0-9._-]{1,64}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Use 1–64 letters, numbers, periods, underscores or hyphens for the username.");
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<ILocalUserRepository>();
        if (await users.FindAsync(name, CancellationToken.None) is not null)
            throw new InvalidOperationException("That administrator already exists; no changes were made.");
        var password = ReadPassword("Password (15–128 characters): ");
        if (password.Length is < 15 or > 128)
            throw new InvalidOperationException("Password must contain 15–128 characters.");
        if (password != ReadPassword("Confirm password: "))
            throw new InvalidOperationException("Passwords do not match.");
        var user = new LocalUser { UserName = name, Role = UserRoles.Admin, SecurityStamp = LocalAuthentication.NewStamp() };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<LocalUser>>().HashPassword(user, password);
        await users.CreateAsync(user, CancellationToken.None);
        Console.WriteLine("Administrator created. Sign in over HTTPS.");
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace && value.Length > 0) value.Length--;
            else if (!char.IsControl(key.KeyChar) && value.Length < 129) value.Append(key.KeyChar);
        }
    }
}
