using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Web.Infrastructure;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace K3SManager.Web.Controllers;

public sealed class WorkloadsController : K3SControllerBase
{
    private const int MaxTailLines = 2000;

    private readonly IWorkloadService _workloads;
    private readonly IAuditedActionRunner _runner;
    private readonly ICurrentActor _actor;

    public WorkloadsController(IWorkloadService workloads, IAuditedActionRunner runner, ICurrentActor actor)
    {
        _workloads = workloads;
        _runner = runner;
        _actor = actor;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scale(
        string namespaceName,
        string name,
        WorkloadKind kind,
        int replicas,
        CancellationToken cancellationToken)
    {
        try
        {
            await _runner.RunAsync(
                _actor.Describe("Scale", kind.ToString(), name, namespaceName, $"replicas={replicas}"),
                ct => _workloads.ScaleAsync(namespaceName, name, kind, replicas, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Success($"{kind} '{name}' scaled to {replicas}."));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
        }

        return RedirectToAction("Details", "Namespaces", new { name = namespaceName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restart(
        string namespaceName,
        string name,
        WorkloadKind kind,
        CancellationToken cancellationToken)
    {
        try
        {
            await _runner.RunAsync(
                _actor.Describe("Restart", kind.ToString(), name, namespaceName),
                ct => _workloads.RestartAsync(namespaceName, name, kind, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Success($"Rolling restart started for {kind} '{name}'."));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
        }

        return RedirectToAction("Details", "Namespaces", new { name = namespaceName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePod(string namespaceName, string podName, CancellationToken cancellationToken)
    {
        try
        {
            await _runner.RunAsync(
                _actor.Describe("DeletePod", "Pod", podName, namespaceName),
                ct => _workloads.DeletePodAsync(namespaceName, podName, ct),
                cancellationToken).ConfigureAwait(false);

            Flash(Alert.Warning($"Pod '{podName}' deleted. Its controller will recreate it if it has one."));
        }
        catch (KubernetesOperationException ex)
        {
            Flash(Alert.Danger(ex.Message));
        }

        return RedirectToAction("Details", "Namespaces", new { name = namespaceName });
    }

    [HttpGet]
    public async Task<IActionResult> Logs(
        string namespaceName,
        string podName,
        string? container,
        int tailLines,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(namespaceName) || string.IsNullOrWhiteSpace(podName))
        {
            return BadRequest();
        }

        try
        {
            var text = await _workloads
                .GetPodLogsAsync(namespaceName, podName, container, tailLines <= 0 ? 200 : Math.Min(tailLines, MaxTailLines), cancellationToken)
                .ConfigureAwait(false);

            return Content(text, "text/plain; charset=utf-8");
        }
        catch (KubernetesOperationException ex)
        {
            return Content(ex.Message, "text/plain; charset=utf-8");
        }
    }
}
