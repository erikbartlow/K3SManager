using K3SManager.Core.Abstractions;
using K3SManager.Web.Infrastructure;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace K3SManager.Web.Controllers;

[Authorize(Roles = K3SManager.Core.Models.UserRoles.Admin)]
public sealed class SettingsController : K3SControllerBase
{
    private readonly ISystemSettingsRepository _settings;
    private readonly IAuditRepository _audit;
    private readonly ICurrentActor _actor;
    private readonly PersistenceState _persistence;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        ISystemSettingsRepository settings,
        IAuditRepository audit,
        ICurrentActor actor,
        PersistenceState persistence,
        ILogger<SettingsController> logger)
    {
        _settings = settings;
        _audit = audit;
        _actor = actor;
        _persistence = persistence;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settingsTask = _settings.GetAllAsync(cancellationToken);
        var auditTask = _audit.GetRecentAsync(50, cancellationToken);

        await Task.WhenAll(settingsTask, auditTask).ConfigureAwait(false);

        return View(new SettingsViewModel
        {
            Settings = await settingsTask.ConfigureAwait(false),
            RecentActions = await auditTask.ConfigureAwait(false),
            PersistenceEnabled = _persistence.Enabled
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SettingUpdateModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!_persistence.Enabled)
        {
            Flash(Alert.Warning("Persistence is disabled, so settings cannot be saved."));
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            Flash(Alert.Danger("That value was rejected."));
            return RedirectToAction(nameof(Index));
        }

        await _settings.SetValueAsync(model.SettingKey, model.SettingValue, cancellationToken).ConfigureAwait(false);

        // The value is never written to the audit detail - one of these keys holds the node token.
        await _audit.WriteAsync(
            _actor.Describe("UpdateSetting", "SystemSetting", model.SettingKey) with { Succeeded = true },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Setting {SettingKey} updated by {Actor}", model.SettingKey, _actor.Name);

        Flash(Alert.Success($"{model.SettingKey} saved."));
        return RedirectToAction(nameof(Index));
    }
}
