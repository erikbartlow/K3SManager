using k8s;
using k8s.Autorest;
using k8s.Models;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Core.Options;
using K3SManager.Kubernetes.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace K3SManager.Kubernetes.Services;

internal sealed class NamespaceService : INamespaceService
{
    private readonly ClusterQueries _queries;
    private readonly INamespaceProfileRepository _profiles;
    private readonly KubernetesOptions _options;
    private readonly ILogger<NamespaceService> _logger;

    public NamespaceService(
        ClusterQueries queries,
        INamespaceProfileRepository profiles,
        IOptions<KubernetesOptions> options,
        ILogger<NamespaceService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _queries = queries;
        _profiles = profiles;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NamespaceSummary>> ListAsync(bool includeSystem, CancellationToken cancellationToken)
    {
        var namespacesTask = _queries.GetNamespacesAsync(cancellationToken);
        var podsTask = _queries.GetPodsAsync(cancellationToken);
        var profilesTask = _profiles.GetAllAsync(cancellationToken);

        await Task.WhenAll(namespacesTask, podsTask, profilesTask).ConfigureAwait(false);

        var namespaces = await namespacesTask.ConfigureAwait(false);
        var pods = await podsTask.ConfigureAwait(false);
        var profiles = await profilesTask.ConfigureAwait(false);

        var podsByNamespace = pods
            .Where(p => !KubernetesMapper.IsTerminal(p.Status?.Phase))
            .GroupBy(p => p.Metadata?.NamespaceProperty ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var results = new List<NamespaceSummary>(namespaces.Count);

        foreach (var item in namespaces)
        {
            var name = item.Metadata?.Name ?? string.Empty;
            var isSystem = IsSystem(name);
            if (isSystem && !includeSystem)
            {
                continue;
            }

            podsByNamespace.TryGetValue(name, out var nsPods);
            nsPods ??= [];

            var requested = nsPods
                .Select(p => ResourceMath.SumRequests(p.Spec))
                .Aggregate((Cpu: 0d, Memory: 0d), (acc, r) => (acc.Cpu + r.Cpu, acc.Memory + r.Memory));

            profiles.TryGetValue(name, out var profile);

            results.Add(new NamespaceSummary
            {
                Name = name,
                Phase = item.Status?.Phase ?? "Unknown",
                CreatedUtc = item.Metadata?.CreationTimestamp,
                IsSystem = isSystem,
                PodCount = nsPods.Count,
                PodsRunning = nsPods.Count(p => string.Equals(p.Status?.Phase, "Running", StringComparison.Ordinal)),
                PodsNotReady = nsPods.Count(p => !IsPodReady(p)),
                CpuRequestedCores = Math.Round(requested.Cpu, 3),
                MemoryRequestedGiB = Math.Round(requested.Memory, 2),
                Labels = new Dictionary<string, string>(item.Metadata?.Labels ?? new Dictionary<string, string>(), StringComparer.Ordinal),
                Profile = profile
            });
        }

        return results
            .OrderByDescending(n => n.Profile?.IsPinned == true)
            .ThenBy(n => n.IsSystem)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<NamespaceDetail?> GetDetailAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var client = _queries.Client;

        try
        {
            var namespaceObject = await client.CoreV1.ReadNamespaceAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);

            var podsTask = client.CoreV1.ListNamespacedPodAsync(name, cancellationToken: cancellationToken);
            var deploymentsTask = client.AppsV1.ListNamespacedDeploymentAsync(name, cancellationToken: cancellationToken);
            var statefulSetsTask = client.AppsV1.ListNamespacedStatefulSetAsync(name, cancellationToken: cancellationToken);
            var daemonSetsTask = client.AppsV1.ListNamespacedDaemonSetAsync(name, cancellationToken: cancellationToken);
            var servicesTask = client.CoreV1.ListNamespacedServiceAsync(name, cancellationToken: cancellationToken);
            var quotasTask = client.CoreV1.ListNamespacedResourceQuotaAsync(name, cancellationToken: cancellationToken);
            var eventsTask = client.CoreV1.ListNamespacedEventAsync(name, limit: 100, cancellationToken: cancellationToken);
            var profileTask = _profiles.GetAsync(name, cancellationToken);

            await Task.WhenAll(
                podsTask,
                deploymentsTask,
                statefulSetsTask,
                daemonSetsTask,
                servicesTask,
                quotasTask,
                eventsTask,
                profileTask).ConfigureAwait(false);

            var pods = (await podsTask.ConfigureAwait(false)).Items?.ToList() ?? [];
            var deployments = (await deploymentsTask.ConfigureAwait(false)).Items?.ToList() ?? [];
            var statefulSets = (await statefulSetsTask.ConfigureAwait(false)).Items?.ToList() ?? [];
            var daemonSets = (await daemonSetsTask.ConfigureAwait(false)).Items?.ToList() ?? [];
            var services = (await servicesTask.ConfigureAwait(false)).Items?.ToList() ?? [];
            var quotas = (await quotasTask.ConfigureAwait(false)).Items?.ToList() ?? [];
            var events = (await eventsTask.ConfigureAwait(false)).Items?.ToList() ?? [];
            var profile = await profileTask.ConfigureAwait(false);

            var livePods = pods.Where(p => !KubernetesMapper.IsTerminal(p.Status?.Phase)).ToList();
            var requested = livePods
                .Select(p => ResourceMath.SumRequests(p.Spec))
                .Aggregate((Cpu: 0d, Memory: 0d), (acc, r) => (acc.Cpu + r.Cpu, acc.Memory + r.Memory));

            var workloads = deployments.Select(KubernetesMapper.ToSummary)
                .Concat(statefulSets.Select(KubernetesMapper.ToSummary))
                .Concat(daemonSets.Select(KubernetesMapper.ToSummary))
                .OrderBy(w => w.Kind)
                .ThenBy(w => w.Name, StringComparer.Ordinal)
                .ToList();

            return new NamespaceDetail
            {
                Summary = new NamespaceSummary
                {
                    Name = name,
                    Phase = namespaceObject.Status?.Phase ?? "Unknown",
                    CreatedUtc = namespaceObject.Metadata?.CreationTimestamp,
                    IsSystem = IsSystem(name),
                    PodCount = livePods.Count,
                    PodsRunning = livePods.Count(p => string.Equals(p.Status?.Phase, "Running", StringComparison.Ordinal)),
                    PodsNotReady = livePods.Count(p => !IsPodReady(p)),
                    DeploymentCount = deployments.Count,
                    StatefulSetCount = statefulSets.Count,
                    DaemonSetCount = daemonSets.Count,
                    ServiceCount = services.Count,
                    CpuRequestedCores = Math.Round(requested.Cpu, 3),
                    MemoryRequestedGiB = Math.Round(requested.Memory, 2),
                    Labels = new Dictionary<string, string>(namespaceObject.Metadata?.Labels ?? new Dictionary<string, string>(), StringComparer.Ordinal),
                    Profile = profile
                },
                Workloads = workloads,
                Pods = pods.Select(KubernetesMapper.ToSummary)
                    .OrderBy(p => p.IsReady)
                    .ThenBy(p => p.Name, StringComparer.Ordinal)
                    .ToList(),
                Services = services.Select(KubernetesMapper.ToSummary).ToList(),
                Quotas = quotas.Select(KubernetesMapper.ToSummary).ToList(),
                Events = events.Select(KubernetesMapper.ToSummary)
                    .OrderByDescending(e => e.LastSeenUtc ?? DateTime.MinValue)
                    .Take(25)
                    .ToList()
            };
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Namespace {Namespace} was requested but does not exist", name);
            return null;
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Reading namespace '{name}'");
        }
    }

    public async Task CreateAsync(NamespaceCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var body = new V1Namespace
        {
            Metadata = new V1ObjectMeta
            {
                Name = request.Name.Trim().ToLowerInvariant(),
                Labels = request.Labels.Count == 0 ? null : new Dictionary<string, string>(request.Labels, StringComparer.Ordinal),
                Annotations = request.Annotations.Count == 0 ? null : new Dictionary<string, string>(request.Annotations, StringComparer.Ordinal)
            }
        };

        try
        {
            await _queries.Client.CoreV1.CreateNamespaceAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            _queries.Invalidate();
            _logger.LogInformation("Namespace {Namespace} created with {LabelCount} labels", body.Metadata.Name, request.Labels.Count);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new KubernetesOperationException($"A namespace named '{body.Metadata.Name}' already exists.", ex) { StatusCode = 409 };
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Creating namespace '{body.Metadata.Name}'");
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (IsSystem(name))
        {
            throw new KubernetesOperationException($"'{name}' is a system namespace and cannot be deleted from this tool.");
        }

        try
        {
            await _queries.Client.CoreV1.DeleteNamespaceAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
            await _profiles.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
            _queries.Invalidate();
            _logger.LogWarning("Namespace {Namespace} deleted along with every object inside it", name);
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Deleting namespace '{name}'");
        }
    }

    public async Task ApplyQuotaAsync(string name, NamespaceQuotaRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasAnyValue)
        {
            throw new KubernetesOperationException("A quota needs at least one limit.");
        }

        var hard = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal);
        AddQuantity(hard, "requests.cpu", request.CpuRequests);
        AddQuantity(hard, "limits.cpu", request.CpuLimits);
        AddQuantity(hard, "requests.memory", request.MemoryRequests);
        AddQuantity(hard, "limits.memory", request.MemoryLimits);
        if (request.PodLimit is > 0)
        {
            hard["pods"] = new ResourceQuantity(request.PodLimit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var body = new V1ResourceQuota
        {
            Metadata = new V1ObjectMeta { Name = request.QuotaName, NamespaceProperty = name },
            Spec = new V1ResourceQuotaSpec { Hard = hard }
        };

        try
        {
            try
            {
                await _queries.Client.CoreV1
                    .ReplaceNamespacedResourceQuotaAsync(body, request.QuotaName, name, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await _queries.Client.CoreV1
                    .CreateNamespacedResourceQuotaAsync(body, name, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _queries.Invalidate();
            _logger.LogInformation("Resource quota {QuotaName} applied to namespace {Namespace}", request.QuotaName, name);
        }
        catch (HttpOperationException ex)
        {
            throw ClusterQueries.Describe(ex, $"Applying a quota to namespace '{name}'");
        }
    }

    private static void AddQuantity(IDictionary<string, ResourceQuantity> target, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            target[key] = new ResourceQuantity(value.Trim());
        }
        catch (FormatException ex)
        {
            throw new KubernetesOperationException($"'{value}' is not a valid quantity for {key} (try 2, 500m, 4Gi).", ex);
        }
        catch (ArgumentException ex)
        {
            throw new KubernetesOperationException($"'{value}' is not a valid quantity for {key} (try 2, 500m, 4Gi).", ex);
        }
    }

    private static bool IsPodReady(V1Pod pod)
    {
        var statuses = pod.Status?.ContainerStatuses;
        if (statuses is null || statuses.Count == 0)
        {
            return string.Equals(pod.Status?.Phase, "Running", StringComparison.Ordinal);
        }

        return statuses.All(s => s.Ready);
    }

    private bool IsSystem(string name) =>
        _options.SystemNamespaces.Contains(name, StringComparer.Ordinal) ||
        name.StartsWith("kube-", StringComparison.Ordinal);
}
