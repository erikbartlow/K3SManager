using k8s.Models;
using K3SManager.Core.Models;

namespace K3SManager.Kubernetes.Internal;

/// <summary>Flattens the API server's object graph into the records the views bind to.</summary>
internal static class KubernetesMapper
{
    private const string ControlPlaneLabel = "node-role.kubernetes.io/control-plane";
    private const string MasterLabel = "node-role.kubernetes.io/master";
    private const string EtcdLabel = "node-role.kubernetes.io/etcd";
    private const string RolePrefix = "node-role.kubernetes.io/";

    public static NodeSummary ToSummary(
        V1Node node,
        IReadOnlyList<V1Pod> podsOnNode,
        (double Cpu, double Memory)? usage)
    {
        var labels = node.Metadata?.Labels ?? new Dictionary<string, string>();
        var conditions = node.Status?.Conditions ?? [];
        var ready = conditions.Any(c =>
            string.Equals(c.Type, "Ready", StringComparison.Ordinal) &&
            string.Equals(c.Status, "True", StringComparison.OrdinalIgnoreCase));

        var requested = podsOnNode
            .Where(p => !IsTerminal(p.Status?.Phase))
            .Select(p => ResourceMath.SumRequests(p.Spec))
            .Aggregate((Cpu: 0d, Memory: 0d), (acc, r) => (acc.Cpu + r.Cpu, acc.Memory + r.Memory));

        var info = node.Status?.NodeInfo;

        return new NodeSummary
        {
            Name = node.Metadata?.Name ?? "(unnamed)",
            IsReady = ready,
            IsControlPlane = labels.ContainsKey(ControlPlaneLabel) || labels.ContainsKey(MasterLabel) || labels.ContainsKey(EtcdLabel),
            IsUnschedulable = node.Spec?.Unschedulable == true,
            Roles = DescribeRoles(labels),
            KubeletVersion = info?.KubeletVersion ?? "unknown",
            ContainerRuntime = info?.ContainerRuntimeVersion ?? string.Empty,
            OsImage = info?.OsImage ?? string.Empty,
            KernelVersion = info?.KernelVersion ?? string.Empty,
            Architecture = info?.Architecture ?? string.Empty,
            InternalIp = node.Status?.Addresses?
                .FirstOrDefault(a => string.Equals(a.Type, "InternalIP", StringComparison.Ordinal))?.Address,
            CreatedUtc = node.Metadata?.CreationTimestamp,
            CpuCapacityCores = ResourceMath.Cores(node.Status?.Capacity),
            CpuAllocatableCores = ResourceMath.Cores(node.Status?.Allocatable),
            CpuRequestedCores = Math.Round(requested.Cpu, 3),
            MemoryCapacityGiB = ResourceMath.GiB(node.Status?.Capacity),
            MemoryAllocatableGiB = ResourceMath.GiB(node.Status?.Allocatable),
            MemoryRequestedGiB = Math.Round(requested.Memory, 2),
            PodCapacity = ResourceMath.Count(node.Status?.Allocatable, "pods"),
            PodCount = podsOnNode.Count(p => !IsTerminal(p.Status?.Phase)),
            CpuUsageCores = usage?.Cpu,
            MemoryUsageGiB = usage?.Memory,
            Conditions = conditions.Select(c => new NodeCondition
            {
                Type = c.Type ?? "Unknown",
                Status = c.Status ?? "Unknown",
                Reason = c.Reason,
                Message = c.Message,
                LastTransitionUtc = c.LastTransitionTime
            }).ToList(),
            Taints = (node.Spec?.Taints ?? []).Select(t => new NodeTaint
            {
                Key = t.Key,
                Value = t.Value,
                Effect = t.Effect
            }).ToList(),
            Labels = new Dictionary<string, string>(labels, StringComparer.Ordinal)
        };
    }

    public static PodSummary ToSummary(V1Pod pod)
    {
        var statuses = pod.Status?.ContainerStatuses ?? [];
        var requests = ResourceMath.SumRequests(pod.Spec);
        var owner = pod.Metadata?.OwnerReferences?.FirstOrDefault();

        return new PodSummary
        {
            Name = pod.Metadata?.Name ?? "(unnamed)",
            Namespace = pod.Metadata?.NamespaceProperty ?? string.Empty,
            Phase = pod.Status?.Phase ?? "Unknown",
            NodeName = pod.Spec?.NodeName,
            PodIp = pod.Status?.PodIP,
            RestartCount = statuses.Sum(s => s.RestartCount),
            ContainersReady = statuses.Count(s => s.Ready),
            ContainerCount = pod.Spec?.Containers?.Count ?? statuses.Count,
            StartedUtc = pod.Status?.StartTime ?? pod.Metadata?.CreationTimestamp,
            CpuRequestedCores = requests.Cpu,
            MemoryRequestedGiB = requests.Memory,
            OwnerKind = owner?.Kind,
            OwnerName = owner?.Name
        };
    }

    public static WorkloadSummary ToSummary(V1Deployment deployment) => new()
    {
        Name = deployment.Metadata?.Name ?? "(unnamed)",
        Namespace = deployment.Metadata?.NamespaceProperty ?? string.Empty,
        Kind = WorkloadKind.Deployment,
        DesiredReplicas = deployment.Spec?.Replicas ?? 0,
        ReadyReplicas = deployment.Status?.ReadyReplicas ?? 0,
        UpdatedReplicas = deployment.Status?.UpdatedReplicas ?? 0,
        AvailableReplicas = deployment.Status?.AvailableReplicas ?? 0,
        Images = DescribeImages(deployment.Spec?.Template?.Spec),
        CreatedUtc = deployment.Metadata?.CreationTimestamp
    };

    public static WorkloadSummary ToSummary(V1StatefulSet statefulSet) => new()
    {
        Name = statefulSet.Metadata?.Name ?? "(unnamed)",
        Namespace = statefulSet.Metadata?.NamespaceProperty ?? string.Empty,
        Kind = WorkloadKind.StatefulSet,
        DesiredReplicas = statefulSet.Spec?.Replicas ?? 0,
        ReadyReplicas = statefulSet.Status?.ReadyReplicas ?? 0,
        UpdatedReplicas = statefulSet.Status?.UpdatedReplicas ?? 0,
        AvailableReplicas = statefulSet.Status?.AvailableReplicas ?? 0,
        Images = DescribeImages(statefulSet.Spec?.Template?.Spec),
        CreatedUtc = statefulSet.Metadata?.CreationTimestamp
    };

    public static WorkloadSummary ToSummary(V1DaemonSet daemonSet) => new()
    {
        Name = daemonSet.Metadata?.Name ?? "(unnamed)",
        Namespace = daemonSet.Metadata?.NamespaceProperty ?? string.Empty,
        Kind = WorkloadKind.DaemonSet,
        DesiredReplicas = daemonSet.Status?.DesiredNumberScheduled ?? 0,
        ReadyReplicas = daemonSet.Status?.NumberReady ?? 0,
        UpdatedReplicas = daemonSet.Status?.UpdatedNumberScheduled ?? 0,
        AvailableReplicas = daemonSet.Status?.NumberAvailable ?? 0,
        Images = DescribeImages(daemonSet.Spec?.Template?.Spec),
        CreatedUtc = daemonSet.Metadata?.CreationTimestamp
    };

    public static ServiceSummary ToSummary(V1Service service) => new()
    {
        Name = service.Metadata?.Name ?? "(unnamed)",
        Namespace = service.Metadata?.NamespaceProperty ?? string.Empty,
        Type = service.Spec?.Type ?? "ClusterIP",
        ClusterIp = service.Spec?.ClusterIP,
        Ports = string.Join(", ", (service.Spec?.Ports ?? []).Select(DescribePort)),
        CreatedUtc = service.Metadata?.CreationTimestamp
    };

    public static ClusterEvent ToSummary(Corev1Event source) => new()
    {
        Namespace = source.Metadata?.NamespaceProperty ?? source.InvolvedObject?.NamespaceProperty ?? string.Empty,
        ObjectKind = source.InvolvedObject?.Kind ?? "Object",
        ObjectName = source.InvolvedObject?.Name ?? "(unknown)",
        Reason = source.Reason ?? string.Empty,
        Message = source.Message ?? string.Empty,
        Type = source.Type ?? "Normal",
        Count = source.Count ?? 1,
        LastSeenUtc = source.LastTimestamp ?? source.EventTime ?? source.Metadata?.CreationTimestamp
    };

    public static ResourceQuotaSummary ToSummary(V1ResourceQuota quota) => new()
    {
        Name = quota.Metadata?.Name ?? "(unnamed)",
        Hard = Flatten(quota.Status?.Hard ?? quota.Spec?.Hard),
        Used = Flatten(quota.Status?.Used)
    };

    public static bool IsTerminal(string? phase) =>
        string.Equals(phase, "Succeeded", StringComparison.Ordinal) ||
        string.Equals(phase, "Failed", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, string> Flatten(IDictionary<string, ResourceQuantity>? source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source is null)
        {
            return result;
        }

        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value?.ToString() ?? string.Empty;
        }

        return result;
    }

    private static string DescribeRoles(IDictionary<string, string> labels)
    {
        var roles = labels.Keys
            .Where(k => k.StartsWith(RolePrefix, StringComparison.Ordinal))
            .Select(k => k[RolePrefix.Length..])
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        return roles.Count == 0 ? "worker" : string.Join(",", roles);
    }

    private static string DescribeImages(V1PodSpec? spec) =>
        spec?.Containers is null
            ? string.Empty
            : string.Join(", ", spec.Containers.Select(c => c.Image).Where(i => !string.IsNullOrEmpty(i)));

    private static string DescribePort(V1ServicePort port)
    {
        var text = port.NodePort is > 0 ? $"{port.Port}:{port.NodePort}" : port.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(port.Protocol) ? text : $"{text}/{port.Protocol}";
    }
}
