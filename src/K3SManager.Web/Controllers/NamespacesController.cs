using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Web.Infrastructure;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace K3SManager.Web.Controllers;

public sealed class NamespacesController : K3SControllerBase
{
    private readonly INamespaceService _namespaces;
    private readonly INamespaceProfileRepository _profiles;
    private readonly IAuditedActionRunner _runner;
    private readonly ICurrentActor _actor;
    private readonly ISafetyGate _safety;
    private readonly PersistenceState _persistence;
    private readonly ILogger<NamespacesController> _logger;

    public NamespacesController(
        INamespaceService namespaces,
        INamespaceProfileRepository profiles,
        IAuditedActionRunner runner,
        ICurrentActor actor,
        ISafetyGate safety,
        PersistenceState persistence,
        ILogger<NamespacesController> logger)
    {
        _namespaces = namespaces;
        _profiles = profiles;
        _runner = runner;
        _actor = actor;
        _safety = safety;
        _persistence = persistence;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(bool includeSystem, string? filter, CancellationToken cancellationToken)
    {
        var namespaces = await _namespaces.ListAsync(includeSystem, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            namespaces = namespaces
                .Where(n => n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            n.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            n.Labels.Any(l => l.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return View(new NamespaceListViewModel
        {
            Namespaces = namespaces,
            IncludeSystem = includeSystem,
            Filter = filter,
            CanDelete = await _safety.IsNamespaceDeleteAllowedAsync(cancellationToken).ConfigureAwait(false)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NotFound();
        }

        var detail = await _namespaces.GetDetailAsync(name, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            Flash(Alert.Warning($"Namespace '{name}' no longer exists."));
            return RedirectToAction(nameof(Index));
        }

        return View(new NamespaceDetailViewModel
        {
            Detail = detail,
            CanDelete = await _safety.IsNamespaceDeleteAllowedAsync(cancellationToken).ConfigureAwait(false),
            PersistenceEnabled = _persistence.Enabled,
            Profile = new NamespaceProfileEditModel
            {
                NamespaceName = detail.Summary.Name,
                Owner = detail.Summary.Profile?.Owner,
                Environment = detail.Summary.Profile?.Environment,
                Description = detail.Summary.Profile?.Description,
                IsPinned = detail.Summary.Profile?.IsPinned ?? false
            },
            Quota = new QuotaEditModel { NamespaceName = detail.Summary.Name }
        });
    }

    [Authorize(Roles = K3SManager.Core.Models.UserRoles.Admin)]
    [HttpGet]
    public IActionResult Create() => View(new NamespaceCreateModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NamespaceCreateModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var labels = ParsePairs(model.Labels);

        try
        {
            await _runner.RunAsync(
                _actor.Describe("Create", "Namespace", model.Name, model.Name, $"{labels.Count} label(s)"),
                ct => _namespaces.CreateAsync(
                    new NamespaceCreateRequest { Name = model.Name, Labels = labels },
                    ct),
                cancellationToken).ConfigureAwait(false);

            if (_persistence.Enabled &&
                (!string.IsNullOrWhiteSpace(model.Owner) ||
                 !string.IsNullOrWhiteSpace(model.Environment) ||
                 !string.IsNullOrWhiteSpace(model.Description)))
            {
                await _profiles.UpsertAsync(
                    new NamespaceProfile
                    {
                        NamespaceName = model.Name,
                        Owner = model.Owner,
                        Environment = model.Environment,
                        Description = model.Description
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            Flash(Alert.Success($"Namespace '{model.Name}' created."));
            return RedirectToAction(nameof(Details), new { name = model.Name });
        }
        catch (KubernetesOperationException ex)
        {
            _logger.LogWarning(ex, "Creating namespace {Namespace} was rejected", model.Name);
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string name, string confirmName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest();
        }

        if (!string.Equals(name, confirmName, StringComparison.Ordinal))
        {
            Flash(Alert.Danger("The typed name did not match, so nothing was deleted."));
            return RedirectToAction(nameof(Details), new { name });
        }

        try
        {
            await _safety.EnsureNamespaceDeleteAllowedAsync(cancellationToken).ConfigureAwait(false);

            await _runner.RunAsync(
                _actor.Describe("Delete", "Namespace", name, name, "operator confirmed by typing the name"),
                ct => _namespaces.DeleteAsync(name, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Warning($"Namespace '{name}' is terminating. Everything inside it is being removed."));
            return RedirectToAction(nameof(Index));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
            return RedirectToAction(nameof(Details), new { name });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProfile(NamespaceProfileEditModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!_persistence.Enabled)
        {
            Flash(Alert.Warning("Persistence is disabled, so namespace metadata cannot be saved."));
            return RedirectToAction(nameof(Details), new { name = model.NamespaceName });
        }

        if (!ModelState.IsValid)
        {
            Flash(Alert.Danger("The metadata could not be saved - check the field lengths."));
            return RedirectToAction(nameof(Details), new { name = model.NamespaceName });
        }

        var existing = await _profiles.GetAsync(model.NamespaceName, cancellationToken).ConfigureAwait(false);
        await _profiles.UpsertAsync(
            new NamespaceProfile
            {
                NamespaceName = model.NamespaceName,
                Alias = existing?.Alias,
                Owner = model.Owner,
                Environment = model.Environment,
                Description = model.Description,
                IsPinned = model.IsPinned
            },
            cancellationToken).ConfigureAwait(false);

        Flash(Alert.Success("Namespace metadata saved."));
        return RedirectToAction(nameof(Details), new { name = model.NamespaceName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAlias(NamespaceAliasEditModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.NamespaceName)) return BadRequest();

        if (!_persistence.Enabled)
        {
            Flash(Alert.Warning("A database connection is needed to save a namespace alias."));
        }
        else if (!ModelState.IsValid)
        {
            Flash(Alert.Danger("The alias must be 128 characters or fewer."));
        }
        else
        {
            var profile = await _profiles.GetAsync(model.NamespaceName, cancellationToken).ConfigureAwait(false)
                ?? new NamespaceProfile { NamespaceName = model.NamespaceName };
            await _profiles.UpsertAsync(profile with
            {
                Alias = string.IsNullOrWhiteSpace(model.Alias) ? null : model.Alias.Trim()
            }, cancellationToken).ConfigureAwait(false);
            Flash(Alert.Success("Namespace alias saved."));
        }

        return RedirectToAction(nameof(Details), new { name = model.NamespaceName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyQuota(QuotaEditModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        try
        {
            await _runner.RunAsync(
                _actor.Describe("ApplyQuota", "ResourceQuota", model.NamespaceName, model.NamespaceName),
                ct => _namespaces.ApplyQuotaAsync(
                    model.NamespaceName,
                    new NamespaceQuotaRequest
                    {
                        CpuRequests = model.CpuRequests,
                        CpuLimits = model.CpuLimits,
                        MemoryRequests = model.MemoryRequests,
                        MemoryLimits = model.MemoryLimits,
                        PodLimit = model.PodLimit
                    },
                    ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Success("Resource quota applied."));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
        }

        return RedirectToAction(nameof(Details), new { name = model.NamespaceName });
    }
}
