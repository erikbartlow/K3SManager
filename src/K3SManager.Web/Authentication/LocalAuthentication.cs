using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace K3SManager.Web.Authentication;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "K3SManager";
    public string Audience { get; set; } = "K3SManager.Web";
    public string SigningKey { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 30;
}

public static class LocalAuthentication
{
    public const string CookieName = "__Host-K3SManager";

    public static IServiceCollection AddLocalAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Authentication:Jwt");
        var settings = section.Get<JwtSettings>() ?? new();
        var problems = new List<string>();
        byte[] key = [];

        if (!section.Exists())
            problems.Add("the Authentication:Jwt section is absent from every configuration source");

        if (string.IsNullOrWhiteSpace(settings.SigningKey))
            problems.Add("SigningKey is empty");
        else if (!TryDecodeKey(settings.SigningKey, out key))
            problems.Add("SigningKey is not valid Base64 (generate one with: openssl rand -base64 32)");
        else if (key.Length < 32)
            problems.Add($"SigningKey decodes to {key.Length} bytes; at least 32 are required (generate one with: openssl rand -base64 32)");

        if (string.IsNullOrWhiteSpace(settings.Issuer))
            problems.Add("Issuer is empty");
        if (string.IsNullOrWhiteSpace(settings.Audience))
            problems.Add("Audience is empty");
        if (settings.LifetimeMinutes is < 5 or > 60)
            problems.Add($"LifetimeMinutes is {settings.LifetimeMinutes}; it must be between 5 and 60");

        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Authentication:Jwt is not configured correctly: " + string.Join("; ", problems) +
                ". Merge Deployment/authentication.example.json into the mounted appsettings.json " +
                "(/app/appsettings.json in the container) or supply Authentication__Jwt__SigningKey, " +
                "Authentication__Jwt__Issuer and Authentication__Jwt__Audience as environment variables. " +
                "The key value itself is never logged.");

        services.AddSingleton(settings);
        services.AddSingleton(new SymmetricSecurityKey(key));
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddSingleton<IPasswordHasher<LocalUser>, PasswordHasher<LocalUser>>();
        services.AddSingleton<PasswordVerifier>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = settings.Issuer,
                ValidateAudience = true, ValidAudience = settings.Audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = JwtRegisteredClaimNames.Sub, RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (!context.Request.Headers.ContainsKey("Authorization"))
                        context.Token = context.Request.Cookies[CookieName];
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var name = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                    var stamp = context.Principal?.FindFirstValue("stamp");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(stamp))
                    {
                        context.Fail("Invalid session.");
                        return;
                    }
                    var users = context.HttpContext.RequestServices.GetRequiredService<ILocalUserRepository>();
                    var user = await users.FindAsync(name, context.HttpContext.RequestAborted);
                    if (user is null || !user.Enabled || user.SecurityStamp != stamp ||
                        !UserRoles.IsValid(user.Role) || context.Principal?.FindFirstValue("role") != user.Role)
                        context.Fail("Invalid session.");
                },
                OnChallenge = context =>
                {
                    // Browser navigation gets a login page; bearer clients and mutations get 401.
                    if (HttpMethods.IsGet(context.Request.Method) &&
                        !context.Request.Headers.ContainsKey("Authorization") &&
                        context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        context.HandleResponse();
                        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                        context.Response.Redirect("/account/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
                    }
                    return Task.CompletedTask;
                }
            };
        });
        services.AddAuthorization(options =>
        {
            var signedIn = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                .RequireRole(UserRoles.Admin, UserRoles.ReadOnly).Build();
            options.AddPolicy("SignedIn", signedIn);
            var access = new AuthorizationPolicyBuilder().Combine(signedIn)
                .RequireAssertion(context => context.User.IsInRole(UserRoles.Admin) ||
                    context.Resource is HttpContext http &&
                    (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) ||
                     HttpMethods.IsOptions(http.Request.Method))).Build();
            options.DefaultPolicy = access;
            options.FallbackPolicy = access;
        });
        // A global login budget cannot be bypassed by spoofing forwarded IP headers.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", _ => RateLimitPartition.GetFixedWindowLimiter("login", _ => new()
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
        });
        return services;
    }

    // Reports malformed Base64 without an exception, so a bad key becomes one clear message
    // rather than a FormatException that hides the other configuration problems.
    private static bool TryDecodeKey(string value, out byte[] key)
    {
        var buffer = new byte[((value.Length + 3) / 4) * 3];
        if (Convert.TryFromBase64String(value, buffer, out var written))
        {
            key = buffer[..written];
            return true;
        }
        key = [];
        return false;
    }

    public static CookieOptions CookieOptions() => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
        Path = "/", IsEssential = true
    };

    public static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

public sealed class PasswordVerifier
{
    private readonly IPasswordHasher<LocalUser> _hasher;
    private readonly LocalUser _dummy = new();
    private readonly string _dummyHash;
    public PasswordVerifier(IPasswordHasher<LocalUser> hasher)
    {
        _hasher = hasher;
        _dummyHash = hasher.HashPassword(_dummy, LocalAuthentication.NewStamp());
    }

    public PasswordVerificationResult Verify(LocalUser? user, string password) =>
        _hasher.VerifyHashedPassword(user ?? _dummy, user?.PasswordHash ?? _dummyHash, password);
}
