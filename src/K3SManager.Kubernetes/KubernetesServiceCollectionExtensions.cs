using k8s;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Options;
using K3SManager.Kubernetes.Internal;
using K3SManager.Kubernetes.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace K3SManager.Kubernetes;

public static class KubernetesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the API server client and the read/write services on top of it.
    /// The client is a singleton: it owns an HttpClient and is thread-safe.
    /// </summary>
    public static IServiceCollection AddK3SManagerKubernetes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<KubernetesOptions>()
            .Bind(configuration.GetSection(KubernetesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IKubernetes>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<KubernetesOptions>>().Value;
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("K3SManager.Kubernetes.Client");
            var config = BuildConfiguration(options, logger);

            var client = new k8s.Kubernetes(config);
            client.HttpClient.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            return client;
        });

        services.AddSingleton<ClusterQueries>();
        services.AddScoped<IClusterService, ClusterService>();
        services.AddScoped<INamespaceService, NamespaceService>();
        services.AddScoped<INodeService, NodeService>();
        services.AddScoped<IWorkloadService, WorkloadService>();

        return services;
    }

    private static KubernetesClientConfiguration BuildConfiguration(KubernetesOptions options, ILogger logger)
    {
        KubernetesClientConfiguration config;

        if (!string.IsNullOrWhiteSpace(options.KubeConfigPath))
        {
            if (!File.Exists(options.KubeConfigPath))
            {
                throw new FileNotFoundException(
                    $"Kubernetes:KubeConfigPath points at '{options.KubeConfigPath}', which does not exist.",
                    options.KubeConfigPath);
            }

            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(
                kubeconfigPath: options.KubeConfigPath,
                currentContext: string.IsNullOrWhiteSpace(options.Context) ? null : options.Context);

            logger.LogInformation(
                "Kubernetes client configured from {KubeConfigPath} (context {Context})",
                options.KubeConfigPath,
                options.Context ?? "default");
        }
        else if (KubernetesClientConfiguration.IsInCluster())
        {
            config = KubernetesClientConfiguration.InClusterConfig();
            logger.LogInformation("Kubernetes client configured from the in-cluster service account");
        }
        else
        {
            config = KubernetesClientConfiguration.BuildDefaultConfig();
            logger.LogInformation("Kubernetes client configured from the ambient default kubeconfig");
        }

        if (!string.IsNullOrWhiteSpace(options.ServerOverrideUrl))
        {
            config.Host = options.ServerOverrideUrl;
            logger.LogInformation("API server host overridden to {ApiServerUrl}", options.ServerOverrideUrl);
        }

        if (options.SkipTlsVerify)
        {
            config.SkipTlsVerify = true;
            logger.LogWarning("TLS verification against the API server is disabled by configuration");
        }

        return config;
    }
}
