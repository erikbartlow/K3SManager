using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;

namespace K3SManager.Web.Infrastructure;

/// <summary>
/// Destructive operations are off unless the operator has switched them on in Settings. The gate
/// is read per request so a setting change takes effect immediately.
/// </summary>
public interface ISafetyGate
{
    Task EnsureNamespaceDeleteAllowedAsync(CancellationToken cancellationToken);

    Task EnsureNodeDrainAllowedAsync(CancellationToken cancellationToken);

    Task<bool> IsNamespaceDeleteAllowedAsync(CancellationToken cancellationToken);

    Task<bool> IsNodeDrainAllowedAsync(CancellationToken cancellationToken);
}

internal sealed class SafetyGate : ISafetyGate
{
    private readonly ISystemSettingsRepository _settings;
    private readonly ILogger<SafetyGate> _logger;

    public SafetyGate(ISystemSettingsRepository settings, ILogger<SafetyGate> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task EnsureNamespaceDeleteAllowedAsync(CancellationToken cancellationToken)
    {
        if (!await IsNamespaceDeleteAllowedAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("A namespace delete was refused because {SettingKey} is not enabled", SettingKeys.AllowNamespaceDelete);
            throw new KubernetesOperationException(
                "Namespace deletion is switched off. Turn on \"Allow namespace delete\" in Settings first.");
        }
    }

    public async Task EnsureNodeDrainAllowedAsync(CancellationToken cancellationToken)
    {
        if (!await IsNodeDrainAllowedAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("A node drain was refused because {SettingKey} is not enabled", SettingKeys.AllowNodeDrain);
            throw new KubernetesOperationException(
                "Cordon and drain are switched off. Turn on \"Allow node drain\" in Settings first.");
        }
    }

    public Task<bool> IsNamespaceDeleteAllowedAsync(CancellationToken cancellationToken) =>
        ReadFlagAsync(SettingKeys.AllowNamespaceDelete, defaultValue: false, cancellationToken);

    public Task<bool> IsNodeDrainAllowedAsync(CancellationToken cancellationToken) =>
        ReadFlagAsync(SettingKeys.AllowNodeDrain, defaultValue: true, cancellationToken);

    private async Task<bool> ReadFlagAsync(string key, bool defaultValue, CancellationToken cancellationToken)
    {
        var raw = await _settings.GetValueAsync(key, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(raw) ? defaultValue : bool.TryParse(raw, out var parsed) && parsed;
    }
}
