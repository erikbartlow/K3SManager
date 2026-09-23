using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Web.Authentication;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K3SManager.Web.Controllers;

[Authorize(Roles = UserRoles.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController(ILocalUserRepository users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View((await users.GetAllAsync(cancellationToken)).Select(user => new UserAccessViewModel
        { UserName = user.UserName, Role = user.Role, Enabled = user.Enabled }).ToList());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(UserAccessViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !UserRoles.IsValid(model.Role)) return BadRequest("Invalid user or role.");
        var user = await users.FindAsync(model.UserName, cancellationToken);
        if (user is null) return NotFound();
        if (user.UserName == User.Identity!.Name && (!model.Enabled || model.Role != UserRoles.Admin))
            return BadRequest("You cannot disable your own account or remove your own Admin role.");
        if (user.Role != model.Role || user.Enabled != model.Enabled)
        {
            user.Role = model.Role;
            user.Enabled = model.Enabled;
            user.SecurityStamp = LocalAuthentication.NewStamp();
            await users.UpdateAsync(user, cancellationToken);
        }
        TempData["UserAccessMessage"] = $"Access updated for {user.UserName}.";
        return RedirectToAction(nameof(Index));
    }
}
