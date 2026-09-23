using System.Diagnostics;
using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace K3SManager.Data.Repositories;

/// <summary>
/// Wraps a mutating cluster call so the audit row is written whether it succeeds or fails.
/// Audit writes never mask the original exception - a failed audit is logged and swallowed.
/// </summary>
internal sealed class AuditedActionRunner : IAuditedActionRunner
{
    private readonly IAuditRepository _audit;
    private readonly ILogger<AuditedActionRunner> _logger;

    public AuditedActionRunner(IAuditRepository audit, ILogger<AuditedActionRunner> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    public async Task RunAsync(AuditEntry entry, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        await RunAsync<object?>(
            entry,
            async ct =>
            {
                await action(ct).ConfigureAwait(false);
                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> RunAsync<T>(AuditEntry entry, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(action);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await action(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            _logger.LogInformation(
                "{Action} on {TargetKind} {TargetName} completed in {ElapsedMs}ms",
                entry.Action,
                entry.TargetKind,
                entry.TargetName,
                stopwatch.ElapsedMilliseconds);

            await SafeWriteAsync(entry with { Succeeded = true, CreatedUtc = DateTime.UtcNow }, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "{Action} on {TargetKind} {TargetName} failed after {ElapsedMs}ms",
                entry.Action,
                entry.TargetKind,
                entry.TargetName,
                stopwatch.ElapsedMilliseconds);

            // The audit write uses CancellationToken.None: a cancelled request must still be recorded.
            await SafeWriteAsync(
                entry with { Succeeded = false, Error = ex.Message, CreatedUtc = DateTime.UtcNow },
                CancellationToken.None).ConfigureAwait(false);

            throw;
        }
    }

    private async Task SafeWriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit row for {Action} on {TargetName} could not be written", entry.Action, entry.TargetName);
        }
    }
}
