using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Web.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace K3SManager.Web.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Anonymous_access_is_denied_but_login_and_health_are_public()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Namespaces")).StatusCode);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        var redirect = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.StartsWith("/account/login?", redirect.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/account/login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
    }

    [Fact]
    public async Task Login_cookie_is_secure_redirect_is_local_and_logout_revokes_token()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        var csrf = await GetCsrf(client);
        var login = await client.PostAsync("/account/login", Form(csrf, "admin", AuthFactory.Password, "https://evil.example/"));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location!.OriginalString);
        var setCookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(LocalAuthentication.CookieName + "="));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        var token = setCookie.Split(';')[0].Split('=', 2)[1];
        var probe = await client.GetAsync("/authentication-test");
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        // A new antiforgery token is required after the identity changes.
        csrf = await GetCsrf(client);
        var logout = await client.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        using var replay = app.Client();
        replay.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/authentication-test")).StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("stamp")]
    [InlineData("disabled")]
    [InlineData("role")]
    public async Task Invalid_or_unauthorized_tokens_are_rejected(string defect)
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        _ = app.Services;
        if (defect == "disabled") app.Users.User.Enabled = false;
        var token = app.Token(defect);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var response = await client.GetAsync("/authentication-test");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_rejects_wrong_password_and_requires_antiforgery()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/login", Form("", "ADMIN", AuthFactory.Password))).StatusCode);
        var csrf = await GetCsrf(client);
        foreach (var name in new[] { "ADMIN", "unknown" })
        {
            var result = await client.PostAsync("/account/login", Form(csrf, name, "wrong password"));
            Assert.Contains("Invalid username or password.", await result.Content.ReadAsStringAsync());
            Assert.False(result.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(x => x.StartsWith(LocalAuthentication.CookieName)));
        }
    }

    [Fact]
    public async Task Login_attempts_are_rate_limited()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        var csrf = await GetCsrf(client);
        for (var i = 0; i < 10; i++)
            await client.PostAsync("/account/login", Form(csrf, "ADMIN", "wrong password"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/account/login", Form(csrf, "ADMIN", "wrong password"))).StatusCode);
    }

    [Fact]
    public async Task Authenticated_mutations_require_antiforgery()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token("valid"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/authentication-test")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/logout", new StringContent(""))).StatusCode);
    }

    [Fact]
    public void Missing_signing_key_is_rejected()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddLocalAuthentication(new ConfigurationBuilder().Build()));
    }

    [Theory]
    [InlineData("/Nodes/Cordon")]
    [InlineData("/Nodes/Drain")]
    [InlineData("/Nodes/AddTaint")]
    [InlineData("/Nodes/RemoveTaint")]
    [InlineData("/Nodes/SetLabel")]
    [InlineData("/Nodes/Remove")]
    [InlineData("/Nodes/Join")]
    [InlineData("/Namespaces/Create")]
    [InlineData("/Namespaces/Delete")]
    [InlineData("/Namespaces/SaveProfile")]
    [InlineData("/Namespaces/ApplyQuota")]
    [InlineData("/Workloads/Scale")]
    [InlineData("/Workloads/Restart")]
    [InlineData("/Workloads/DeletePod")]
    [InlineData("/Settings/Save")]
    [InlineData("/Users/Save")]
    public async Task ReadOnly_cannot_mutate_even_with_valid_antiforgery(string path)
    {
        await using var app = new AuthFactory();
        app.Users.User.Role = UserRoles.ReadOnly;
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token("valid"));
        var csrf = await GetCsrf(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(path, Form(csrf, "ADMIN", "unused"))).StatusCode);
    }

    [Theory]
    [InlineData("/Settings")]
    [InlineData("/Users")]
    [InlineData("/Nodes/Join")]
    [InlineData("/Namespaces/Create")]
    public async Task ReadOnly_cannot_open_administration_pages(string path)
    {
        await using var app = new AuthFactory();
        app.Users.User.Role = UserRoles.ReadOnly;
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token("valid"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task ReadOnly_can_read_and_sign_out_and_has_no_admin_navigation()
    {
        await using var app = new AuthFactory();
        app.Users.User.Role = UserRoles.ReadOnly;
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token("valid"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/authentication-test")).StatusCode);
        var html = await client.GetStringAsync("/account/login");
        Assert.DoesNotContain("href=\"/Settings\"", html);
        Assert.DoesNotContain("href=\"/Users\"", html);
        var csrf = await GetCsrf(client);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/account/logout", Form(csrf, "ADMIN", "unused"))).StatusCode);
    }

    [Fact]
    public async Task Database_role_change_rejects_old_admin_token()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token("valid"));
        app.Users.User.Role = UserRoles.ReadOnly;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/authentication-test")).StatusCode);
    }

    [Fact]
    public async Task Admin_can_assign_access_but_cannot_demote_self_or_assign_unknown_role()
    {
        await using var app = new AuthFactory();
        var pending = new LocalUser { UserName = "PENDING", Enabled = false, SecurityStamp = "old", PasswordHash = "unchanged" };
        await app.Users.CreateAsync(pending, default);
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token("valid"));
        var html = await client.GetStringAsync("/Users");
        Assert.Contains("PENDING", html);
        Assert.DoesNotContain("unchanged", html);
        var csrf = await GetCsrf(client);
        FormUrlEncodedContent Access(string user, string role, bool enabled) => new(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = csrf, ["UserName"] = user, ["Role"] = role, ["Enabled"] = enabled.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Users/Save", Access("ADMIN", UserRoles.ReadOnly, true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Users/Save", Access("PENDING", "Owner", true))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Users/Save", Access("PENDING", UserRoles.Admin, true))).StatusCode);
        Assert.True(pending.Enabled);
        Assert.Equal(UserRoles.Admin, pending.Role);
        Assert.NotEqual("old", pending.SecurityStamp);
        Assert.Equal("unchanged", pending.PasswordHash);
    }

    private static async Task<string> GetCsrf(HttpClient client)
    {
        var html = await client.GetStringAsync("/account/login");
        return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }

    [Fact]
    public async Task Registration_database_failure_returns_500_not_authentication_challenge()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        var csrf = await GetCsrf(client);
        app.Users.FailReads = true;
        var result = await client.PostAsync("/account/register", Registration(csrf, "new.user", AuthFactory.Password, AuthFactory.Password));
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Empty(result.Headers.WwwAuthenticate);
        Assert.Contains("That request did not complete", await result.Content.ReadAsStringAsync());
        Assert.DoesNotContain("test database unavailable", await result.Content.ReadAsStringAsync());
        Assert.Null(app.Users.Created);
    }

    [Fact]
    public async Task Registration_creates_disabled_hashed_user_and_cannot_sign_in()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        var loginHtml = await client.GetStringAsync("/account/login");
        Assert.Contains("/account/register", loginHtml);
        Assert.Contains("auth-card", loginHtml);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/account/register")).StatusCode);
        var csrf = await GetCsrf(client);
        var response = await client.PostAsync("/account/register", Registration(csrf, "new.user", AuthFactory.Password, AuthFactory.Password));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var user = Assert.IsType<LocalUser>(app.Users.Created);
        Assert.Equal("NEW.USER", user.UserName);
        Assert.False(user.Enabled);
        Assert.Equal(UserRoles.ReadOnly, user.Role);
        Assert.NotEqual(AuthFactory.Password, user.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<LocalUser>().VerifyHashedPassword(user, user.PasswordHash, AuthFactory.Password));
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(x => x.StartsWith(LocalAuthentication.CookieName)));
        Assert.Contains("administrator must enable", await client.GetStringAsync("/account/login"));
        csrf = await GetCsrf(client);
        var denied = await client.PostAsync("/account/login", Form(csrf, "new.user", AuthFactory.Password));
        Assert.Contains("Invalid username or password.", await denied.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("short", "short")]
    [InlineData(AuthFactory.Password, "different password")]
    public async Task Registration_validates_password_without_creating_account(string password, string confirmation)
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        var csrf = await GetCsrf(client);
        var response = await client.PostAsync("/account/register", Registration(csrf, "new.user", password, confirmation));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(app.Users.Created);
    }

    [Fact]
    public async Task Registration_requires_antiforgery_and_rejects_duplicate_names()
    {
        await using var app = new AuthFactory();
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/register", Registration("", "new", AuthFactory.Password, AuthFactory.Password))).StatusCode);
        var csrf = await GetCsrf(client);
        var response = await client.PostAsync("/account/register", Registration(csrf, "admin", AuthFactory.Password, AuthFactory.Password));
        Assert.Contains("That username is unavailable.", await response.Content.ReadAsStringAsync());
        Assert.Null(app.Users.Created);
        Assert.True(app.Users.User.Enabled);
    }

    private static FormUrlEncodedContent Registration(string csrf, string name, string password, string confirmation) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = csrf, ["UserName"] = name, ["Password"] = password,
        ["ConfirmPassword"] = confirmation, ["Role"] = "Admin", ["Enabled"] = "true", ["SecurityStamp"] = "attacker-controlled"
    });
    private static FormUrlEncodedContent Form(string csrf, string user, string password, string returnUrl = "/") => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = csrf, ["UserName"] = user, ["Password"] = password, ["ReturnUrl"] = returnUrl
    });
}

public sealed class AuthFactory : WebApplicationFactory<Program>
{
    public const string Password = "Test-only password 987!";
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    public FakeUsers Users { get; } = new();
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "k3smanager-auth-tests-" + Guid.NewGuid().ToString("N"));

    public AuthFactory()
    {
        Directory.CreateDirectory(_contentRoot);
        File.WriteAllText(Path.Combine(_contentRoot, "appsettings.json"), JsonSerializer.Serialize(new
        {
            Data = new { Enabled = true, ConnectionName = "TEST" },
            CoreSettings = new { Connections = new[] { new
            {
                Name = "TEST", Primary = true, ProviderType = "MySql",
                ConnectionString = "Server=unused.invalid;Database=unused;"
            } } }
        }));
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment("Production");
        builder.UseSetting("Authentication:Jwt:SigningKey", Convert.ToBase64String(_key));
        builder.UseSetting("Authentication:Jwt:Issuer", "test");
        builder.UseSetting("Authentication:Jwt:Audience", "test-web");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Jwt:SigningKey"] = Convert.ToBase64String(_key),
            ["Authentication:Jwt:Issuer"] = "test", ["Authentication:Jwt:Audience"] = "test-web"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILocalUserRepository>();
            services.AddSingleton<ILocalUserRepository>(Users);
            services.AddControllersWithViews().AddApplicationPart(typeof(AuthenticationProbeController).Assembly);
        });
    }
    public HttpClient Client() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_contentRoot))
        {
            File.Delete(Path.Combine(_contentRoot, "appsettings.json"));
            Directory.Delete(_contentRoot);
        }
    }
    public string Token(string defect)
    {
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(defect == "issuer" ? "wrong" : "test", defect == "audience" ? "wrong" : "test-web",
            [new Claim("sub", "ADMIN"), new Claim("stamp", defect == "stamp" ? "wrong" : Users.User.SecurityStamp),
             new Claim("role", defect == "role" ? "Reader" : Users.User.Role)],
            now.AddHours(-1), defect == "expired" ? now.AddMinutes(-5) : now.AddMinutes(5),
            new SigningCredentials(new SymmetricSecurityKey(defect == "signature" ? RandomNumberGenerator.GetBytes(32) : _key), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

public sealed class FakeUsers : ILocalUserRepository
{
    public Task<IReadOnlyList<LocalUser>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LocalUser>>(Created is null ? [User] : [User, Created]);
    public bool FailReads { get; set; }
    public LocalUser? Created { get; private set; }
    public LocalUser User { get; } = new() { UserName = "ADMIN", Role = UserRoles.Admin, SecurityStamp = LocalAuthentication.NewStamp() };
    public FakeUsers() => User.PasswordHash = new PasswordHasher<LocalUser>().HashPassword(User, AuthFactory.Password);
    public Task<LocalUser?> FindAsync(string userName, CancellationToken cancellationToken) =>
        FailReads ? throw new InvalidOperationException("test database unavailable") : Task.FromResult(userName == User.UserName ? User : Created?.UserName == userName ? Created : null);
    public Task CreateAsync(LocalUser user, CancellationToken cancellationToken)
    {
        Created = user;
        return Task.CompletedTask;
    }
    public Task UpdateAsync(LocalUser user, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class AuthenticationProbeController : Controller
{
    [HttpGet("/authentication-test")]
    public IActionResult Get() => Ok(User.Identity!.Name);
}
