using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Web.Authentication;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace K3SManager.Web.Controllers;

[Route("account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(
    ILocalUserRepository users, PasswordVerifier verifier, IPasswordHasher<LocalUser> hasher,
    JwtSettings settings, SymmetricSecurityKey key) : Controller
{
    [AllowAnonymous, HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpGet("register")]
    public IActionResult Register() => View(new RegisterViewModel());

    [AllowAnonymous, HttpPost("register"), ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest("HTTPS is required to register.");
        if (!ModelState.IsValid) return View(model);

        var name = model.UserName.ToUpperInvariant();
        if (await users.FindAsync(name, cancellationToken) is not null)
        {
            ModelState.AddModelError(nameof(model.UserName), "That username is unavailable.");
            return View(model);
        }

        var user = new LocalUser
        {
            UserName = name,
            Enabled = false,
            Role = UserRoles.ReadOnly,
            SecurityStamp = LocalAuthentication.NewStamp()
        };
        user.PasswordHash = hasher.HashPassword(user, model.Password);
        try
        {
            await users.CreateAsync(user, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A competing registration can insert the same primary key after the lookup.
            // Only translate that case; other database failures must remain visible to operators.
            if (await users.FindAsync(name, cancellationToken) is null) throw;
            ModelState.AddModelError(nameof(model.UserName), "That username is unavailable.");
            return View(model);
        }
        TempData["RegistrationMessage"] = "Your account has been created, but it is currently disabled. An administrator must enable your account before you can sign in. Please contact your administrator to request access.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpPost("login"), ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest("HTTPS is required to sign in.");
        if (!ModelState.IsValid) return View(model);
        var user = await users.FindAsync(model.UserName.ToUpperInvariant(), cancellationToken);
        var result = verifier.Verify(user, model.Password);
        if (user is null || !user.Enabled || !UserRoles.IsValid(user.Role) || result == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            model.Password = string.Empty;
            ModelState.Remove(nameof(model.Password));
            return View(model);
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, model.Password);
            await users.UpdateAsync(user, cancellationToken);
        }
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.UserName), new Claim("role", user.Role),
             new Claim("stamp", user.SecurityStamp), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))],
            now, expires, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var cookie = LocalAuthentication.CookieOptions();
        cookie.Expires = expires;
        Response.Cookies.Append(LocalAuthentication.CookieName, new JwtSecurityTokenHandler().WriteToken(token), cookie);
        return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
    }

    [Authorize(Policy = "SignedIn"), HttpPost("logout"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(User.Identity!.Name!, cancellationToken);
        if (user is not null)
        {
            // Revokes all outstanding JWTs for this user, including copied tokens.
            user.SecurityStamp = LocalAuthentication.NewStamp();
            await users.UpdateAsync(user, cancellationToken);
        }
        Response.Cookies.Delete(LocalAuthentication.CookieName, LocalAuthentication.CookieOptions());
        return RedirectToAction(nameof(Login));
    }
}
