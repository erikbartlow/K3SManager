using CodedThought.Core.Configuration;
using CodedThought.Core.Data;
using K3SManager.Core.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace K3SManager.Data.Internal;

/// <summary>
/// One CodedThought.Core data store, owned by one repository.
///
/// Registered transient, so each repository resolves its own. The host registers
/// <c>IDatabaseObject</c> as transient too, which means each store gets its own provider and
/// therefore its own connection: repositories running concurrently do not collide, and the
/// connection is closed when the request scope disposes the repository.
///
/// The gate below is per instance, not per process. It exists only because a single repository can
/// still be called concurrently - <c>NodeService.BuildJoinInstructionAsync</c> reads three settings
/// under one <c>Task.WhenAll</c> - and those calls share this store's one connection.
///
/// <c>GenericDataStore</c> is synchronous, so the work is dispatched to the thread pool: the
/// request thread is released and a pool thread blocks on the socket. If the framework gains an
/// async surface, this class is the only thing that changes.
/// </summary>
internal sealed class CoreDataStore : GenericDataStoreController, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _cache;
    private readonly ConnectionSetting _connection;
    private readonly ILogger<CoreDataStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _commandTimeout;

    private bool _disposed;

    public CoreDataStore(
        IServiceProvider serviceProvider,
        IMemoryCache cache,
        ConnectionSetting connection,
        IOptions<DataOptions> options,
        ILogger<CoreDataStore> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        _serviceProvider = serviceProvider;
        _cache = cache;
        _logger = logger;
        _commandTimeout = options.Value.CommandTimeoutSeconds;

        _connection = connection;

        ConnectionName = _connection.Name;
    }

    /// <summary>Runs a read against the data store and returns its result.</summary>
    public Task<TResult> ReadAsync<TResult>(Func<GenericDataStore, TResult> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return ExecuteAsync(work, cancellationToken);
    }

    /// <summary>Runs a write against the data store.</summary>
    public Task WriteAsync(Action<GenericDataStore> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        return ExecuteAsync<object?>(
            store =>
            {
                work(store);
                return null;
            },
            cancellationToken);
    }

    private async Task<TResult> ExecuteAsync<TResult>(Func<GenericDataStore, TResult> work, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The token is passed to Task.Run so an operation still queued when the request is
            // abandoned never starts. Once the framework call is running it cannot be interrupted.
            return await Task.Run(() => work(EnsureStore()), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Built on first use, under the gate. Construction resolves a provider, opens its connection
    /// and loads the ORM map for every assembly marked with <c>DataAwareAssembly</c>, so it must
    /// not happen at startup where a database that is not up yet would take the whole app down.
    /// </summary>
    private GenericDataStore EnsureStore()
    {
        if (DataStore is not null)
        {
            return DataStore;
        }

        try
        {
            DataStore = new GenericDataStore(_serviceProvider, _cache, _connection)
            {
                CommandTimeout = _commandTimeout
            };
        }
        catch (Exception ex)
        {
            // The framework embeds the connection string in its exception message.
            // Keep credentials out of application logs and the exception handler.
            _logger.LogError("Opening the CodedThought.Core data store failed ({ErrorType}). Check database configuration and connectivity.", ex.GetBaseException().GetType().Name);
            throw new InvalidOperationException("Database access is unavailable. Check database configuration, connectivity and schema.");
        }

        _logger.LogDebug(
            "CodedThought.Core data store opened on connection {ConnectionName} using the {ProviderType} provider",
            _connection.Name,
            _connection.ProviderType);

        return DataStore;
    }

    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            base.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Closing the CodedThought.Core data store did not complete cleanly");
        }

        _gate.Dispose();
    }
}
