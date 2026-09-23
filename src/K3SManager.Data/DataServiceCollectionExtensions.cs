using CodedThought.Core.Configuration;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Options;
using K3SManager.Data.Internal;
using K3SManager.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace K3SManager.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the repositories. The host owns the CodedThought.Core provider registration and
    /// the memory cache, because the provider's lifetime is a hosting decision - see Program.cs.
    /// When Data:Enabled is false, or no usable CoreSettings connection is configured, no-op
    /// repositories are registered instead and the app runs as a read-only cluster viewer.
    /// </summary>
    public static IServiceCollection AddK3SManagerData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<DataOptions>(configuration.GetSection(DataOptions.SectionName));
        services.AddScoped<IAuditedActionRunner, AuditedActionRunner>();

        var options = ReadOptions(configuration);
        var connection = options.Enabled ? ResolveConnection(configuration, options) : null;

        if (connection is null || string.IsNullOrWhiteSpace(connection.ConnectionString))
        {
            services.AddSingleton<ISystemSettingsRepository, NullSystemSettingsRepository>();
            services.AddSingleton<IAuditRepository, NullAuditRepository>();
            services.AddSingleton<INamespaceProfileRepository, NullNamespaceProfileRepository>();
            return services;
        }

        AssertProviderIsReferenced(connection);

        // Keep the framework-resolved connection used to enable persistence and the
        // connection passed to every GenericDataStore consistent for this app lifetime.
        services.AddSingleton(connection);

        // Transient: each repository gets its own store, its own provider and its own connection,
        // so repositories running concurrently do not share one. The request scope disposes them.
        services.AddTransient<CoreDataStore>();
        services.AddScoped<ILocalUserRepository, LocalUserRepository>();

        services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<INamespaceProfileRepository, NamespaceProfileRepository>();

        return services;
    }

    /// <summary>True when the app is configured to persist anything at all.</summary>
    public static bool IsPersistenceConfigured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = ReadOptions(configuration);
        if (!options.Enabled)
        {
            return false;
        }

        var connection = ResolveConnection(configuration, options);
        return connection is not null && !string.IsNullOrWhiteSpace(connection.ConnectionString);
    }

    private static DataOptions ReadOptions(IConfiguration configuration) =>
        configuration.GetSection(DataOptions.SectionName).Get<DataOptions>() ?? new DataOptions();

    private static ConnectionSetting? ResolveConnection(IConfiguration configuration, DataOptions options) =>
        CoreConnectionResolver.Resolve(configuration, options);

    /// <summary>
    /// CodedThought.Core matches a provider to a connection by its ProviderType. The host only
    /// registers the MySql provider, so a connection configured for anything else fails at startup
    /// with a message rather than at the first query with a null reference.
    /// </summary>
    private static void AssertProviderIsReferenced(ConnectionSetting connection)
    {
        if (!string.Equals(connection.ProviderType, "MySql", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Connection '{connection.Name}' is configured for provider '{connection.ProviderType}', " +
                "but the host only registers the MySql provider. Add the matching " +
                "CodedThought.Core.Data.* package to K3SManager.Web and register its IDatabaseObject " +
                "in Program.cs first.");
        }
    }
}
