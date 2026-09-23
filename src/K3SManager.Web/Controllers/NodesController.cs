using System.Globalization;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Web.Infrastructure;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace K3SManager.Web.Controllers;

public sealed class NodesController : K3SControllerBase
{
    private readonly INodeService _nodes;
    private readonly IAuditedActionRunner _runner;
    private readonly ICurrentActor _actor;
    private readonly ISafetyGate _safety;
    private readonly ILogger<NodesController> _logger;

    public NodesController(
        INodeService nodes,
        IAuditedActionRunner runner,
        ICurrentActor actor,
        ISafetyGate safety,
        ILogger<NodesController> logger)
    {
        _nodes = nodes;
        _runner = runner;
        _actor = actor;
        _safety = safety;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new NodeListViewModel
        {
            Nodes = await _nodes.ListAsync(cancellationToken).ConfigureAwait(false),
            CanDrain = await _safety.IsNodeDrainAllowedAsync(cancellationToken).ConfigureAwait(false)
        });

    [HttpGet]
    public async Task<IActionResult> Details(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NotFound();
        }

        var node = await _nodes.GetAsync(name, cancellationToken).ConfigureAwait(false);
        if (node is null)
        {
            Flash(Alert.Warning($"Node '{name}' is not registered with this cluster."));
            return RedirectToAction(nameof(Index));
        }

        return View(new NodeDetailViewModel
        {
            Node = node,
            Pods = await _nodes.GetPodsAsync(name, cancellationToken).ConfigureAwait(false),
            CanDrain = await _safety.IsNodeDrainAllowedAsync(cancellationToken).ConfigureAwait(false),
            Taint = new TaintInputModel { NodeName = name },
            Label = new LabelInputModel { NodeName = name }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cordon(string name, bool schedulable, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest();
        }

        try
        {
            await _safety.EnsureNodeDrainAllowedAsync(cancellationToken).ConfigureAwait(false);

            await _runner.RunAsync(
                _actor.Describe(schedulable ? "Uncordon" : "Cordon", "Node", name),
                ct => _nodes.SetSchedulableAsync(name, schedulable, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(schedulable
                ? Alert.Success($"Node '{name}' is schedulable again.")
                : Alert.Warning($"Node '{name}' is cordoned. Existing pods keep running; no new ones will be scheduled."));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
        }

        return RedirectToAction(nameof(Details), new { name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Drain(
        string name,
        bool ignoreDaemonSets,
        bool force,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest();
        }

        try
        {
            await _safety.EnsureNodeDrainAllowedAsync(cancellationToken).ConfigureAwait(false);

            var evicted = await _runner.RunAsync(
                _actor.Describe("Drain", "Node", name, detail: $"ignoreDaemonSets={ignoreDaemonSets}; force={force}"),
                ct => _nodes.DrainAsync(
                    name,
                    new DrainOptions { IgnoreDaemonSets = ignoreDaemonSets, Force = force },
                    ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Success(
                $"Node '{name}' drained - {evicted.ToString(CultureInfo.InvariantCulture)} pod(s) evicted. It stays cordoned until you uncordon it."));
        }
        catch (KubernetesOperationException ex)
        {
            _logger.LogWarning(ex, "Drain of node {NodeName} did not complete cleanly", name);
            Flash(Alert.Warning(ex.Message));
        }

        return RedirectToAction(nameof(Details), new { name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTaint(TaintInputModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            Flash(Alert.Danger("The taint was not valid."));
            return RedirectToAction(nameof(Details), new { name = model.NodeName });
        }

        try
        {
            var taint = new NodeTaint { Key = model.Key, Value = model.Value, Effect = model.Effect };

            await _runner.RunAsync(
                _actor.Describe("AddTaint", "Node", model.NodeName, detail: taint.ToString()),
                ct => _nodes.AddTaintAsync(model.NodeName, taint, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Success($"Taint {taint} added."));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
        }

        return RedirectToAction(nameof(Details), new { name = model.NodeName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTaint(string name, string key, string effect, CancellationToken cancellationToken)
    {
        try
        {
            await _runner.RunAsync(
                _actor.Describe("RemoveTaint", "Node", name, detail: $"{key}:{effect}"),
                ct => _nodes.RemoveTaintAsync(name, key, effect, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Success($"Taint {key}:{effect} removed."));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
        }

        return RedirectToAction(nameof(Details), new { name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLabel(LabelInputModel model, bool remove, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            Flash(Alert.Danger("The label was not valid."));
            return RedirectToAction(nameof(Details), new { name = model.NodeName });
        }

        try
        {
            await _runner.RunAsync(
                _actor.Describe(remove ? "RemoveLabel" : "SetLabel", "Node", model.NodeName, detail: model.Key),
                ct => _nodes.SetLabelAsync(model.NodeName, model.Key, remove ? null : model.Value ?? string.Empty, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Success(remove ? $"Label {model.Key} removed." : $"Label {model.Key} set."));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
        }

        return RedirectToAction(nameof(Details), new { name = model.NodeName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(string name, string confirmName, CancellationToken cancellationToken)
    {
        if (!string.Equals(name, confirmName, StringComparison.Ordinal))
        {
            Flash(Alert.Danger("The typed name did not match, so the node was left alone."));
            return RedirectToAction(nameof(Details), new { name });
        }

        try
        {
            await _safety.EnsureNodeDrainAllowedAsync(cancellationToken).ConfigureAwait(false);

            await _runner.RunAsync(
                _actor.Describe("RemoveNode", "Node", name, detail: "operator confirmed by typing the name"),
                ct => _nodes.RemoveAsync(name, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Warning(
                $"Node '{name}' was removed from the cluster. Run k3s-agent-uninstall.sh on the machine itself, or it will re-register."));
            return RedirectToAction(nameof(Index));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
            return RedirectToAction(nameof(Details), new { name });
        }
    }

    [Authorize(Roles = K3SManager.Core.Models.UserRoles.Admin)]
    [HttpGet]
    public async Task<IActionResult> Join(CancellationToken cancellationToken)
    {
        var model = new NodeJoinViewModel
        {
            Instruction = await _nodes.BuildJoinInstructionAsync(new JoinRequest(), cancellationToken).ConfigureAwait(false)
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Join(NodeJoinViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.Instruction = await _nodes.BuildJoinInstructionAsync(
            new JoinRequest
            {
                NodeName = model.NodeName,
                Labels = ParseList(model.Labels),
                Taints = ParseList(model.Taints)
            },
            cancellationToken).ConfigureAwait(false);

        return View(model);
    }
}
