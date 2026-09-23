using k8s.Models;

namespace K3SManager.Kubernetes.Internal;

/// <summary>
/// Kubernetes quantities are decimal/binary SI strings ("250m", "2Gi"). ResourceQuantity already
/// parses them; this normalises the results into the units the UI displays.
/// </summary>
internal static class ResourceMath
{
    private const decimal BytesPerGiB = 1024m * 1024m * 1024m;

    public static double Cores(IDictionary<string, ResourceQuantity>? resources, string key = "cpu")
    {
        if (resources is null || !resources.TryGetValue(key, out var quantity) || quantity is null)
        {
            return 0d;
        }

        return ToDouble(quantity);
    }

    public static double GiB(IDictionary<string, ResourceQuantity>? resources, string key = "memory")
    {
        if (resources is null || !resources.TryGetValue(key, out var quantity) || quantity is null)
        {
            return 0d;
        }

        return BytesToGiB(quantity);
    }

    public static int Count(IDictionary<string, ResourceQuantity>? resources, string key)
    {
        if (resources is null || !resources.TryGetValue(key, out var quantity) || quantity is null)
        {
            return 0;
        }

        return (int)Math.Round(ToDouble(quantity), MidpointRounding.AwayFromZero);
    }

    /// <summary>Sums container resource requests across a pod spec.</summary>
    public static (double Cpu, double Memory) SumRequests(V1PodSpec? spec)
    {
        if (spec?.Containers is null)
        {
            return (0d, 0d);
        }

        var cpu = 0d;
        var memoryBytes = 0m;

        foreach (var container in spec.Containers)
        {
            var requests = container.Resources?.Requests;
            if (requests is null)
            {
                continue;
            }

            cpu += Cores(requests);
            if (requests.TryGetValue("memory", out var memory) && memory is not null)
            {
                memoryBytes += ToDecimalSafe(memory);
            }
        }

        return (Math.Round(cpu, 3), Math.Round((double)(memoryBytes / BytesPerGiB), 2));
    }

    public static double ToDouble(ResourceQuantity quantity) => (double)ToDecimalSafe(quantity);

    public static double BytesToGiB(ResourceQuantity quantity) =>
        Math.Round((double)(ToDecimalSafe(quantity) / BytesPerGiB), 2);

    private static decimal ToDecimalSafe(ResourceQuantity quantity)
    {
        try
        {
            return quantity.ToDecimal();
        }
        catch (Exception)
        {
            // A malformed quantity from the API server must not take down a whole list page.
            return 0m;
        }
    }
}
