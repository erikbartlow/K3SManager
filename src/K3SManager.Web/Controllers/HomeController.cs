using System.Diagnostics;
using System.Globalization;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Web.Infrastructure;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace K3SManager.Web.Controllers;

public sealed class HomeController : K3SControllerBase
{
    private const int DefaultRefreshSeconds = 30;

    private readonly IClusterService _cluster;
    private readonly INodeService _nodes;
    private readonly INamespaceService _namespaces;
    private readonly ISystemSettingsRepository _settings;
    private readonly IAuditRepository _audit;
    private readonly PersistenceState _persistence;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IClusterService cluster,
        INodeService nodes,
        INamespaceService namespaces,
        ISystemSettingsRepository settings,
        IAuditRepository audit,
        PersistenceState persistence,
        ILogger<HomeController> logger)
    {
        _cluster = cluster;
        _nodes = nodes;
        _namespaces = namespaces;
        _settings = settings;
        _audit = audit;
        _persistence = persistence;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var clusterTask = _cluster.GetSummaryAsync(cancellationToken);
        var nodesTask = _nodes.ListAsync(cancellationToken);
        var namespacesTask = _namespaces.ListAsync(includeSystem: false, cancellationToken);
        var refreshTask = _settings.GetValueAsync(SettingKeys.UiRefreshSeconds, cancellationToken);
        var auditTask = _audit.GetRecentAsync(8, cancellationToken);

        await Task.WhenAll(clusterTask, nodesTask, namespacesTask, refreshTask, auditTask).ConfigureAwait(false);

        var refreshRaw = await refreshTask.ConfigureAwait(false);
        var refreshSeconds = int.TryParse(refreshRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 3600)
            : DefaultRefreshSeconds;

        var model = new DashboardViewModel
        {
            Cluster = await clusterTask.ConfigureAwait(false),
            Nodes = await nodesTask.ConfigureAwait(false),
            Namespaces = await namespacesTask.ConfigureAwait(false),
            RecentActions = await auditTask.ConfigureAwait(false),
            RefreshSeconds = refreshSeconds,
            PersistenceEnabled = _persistence.Enabled
        };

        _logger.LogDebug("Dashboard rendered for {NodeCount} nodes", model.Nodes.Count);

        if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return PartialView("_Dashboard", model);
        }

        return View(model);
    }

}
