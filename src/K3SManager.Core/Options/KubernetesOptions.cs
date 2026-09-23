using System.ComponentModel.DataAnnotations;

namespace K3SManager.Core.Options;

/// <summary>Binding target for the "Kubernetes" configuration section.</summary>
public sealed class KubernetesOptions
{
    public const string SectionName = "Kubernetes";

    /// <summary>
    /// Path to the kubeconfig used to reach the cluster. For a k3s server this is usually
    /// /etc/rancher/k3s/k3s.yaml. Leave empty to use in-cluster config (when hosted on the
    /// cluster itself) falling back to the ambient default kubeconfig.
    /// </summary>
    public string? KubeConfigPath { get; init; }

    /// <summary>Optional named context inside the kubeconfig.</summary>
    public string? Context { get; init; }

    /// <summary>Overrides the server URL found in the kubeconfig. Useful when the file says 127.0.0.1.</summary>
    public string? ServerOverrideUrl { get; init; }

    /// <summary>Accept a self-signed API server certificate. k3s defaults make this common on a home cluster.</summary>
    public bool SkipTlsVerify { get; init; }

    /// <summary>How long list results are cached before the API server is hit again.</summary>
    [Range(0, 300)]
    public int CacheSeconds { get; init; } = 10;

    /// <summary>Per-request timeout against the API server.</summary>
    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; init; } = 30;

    /// <summary>Namespaces hidden from the namespace list unless "show system" is toggled on.</summary>
    public IReadOnlyList<string> SystemNamespaces { get; init; } =
    [
        "kube-system",
        "kube-public",
        "kube-node-lease",
        "default"
    ];
}
