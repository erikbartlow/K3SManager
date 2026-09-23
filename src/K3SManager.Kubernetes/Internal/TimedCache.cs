using System.Collections.Concurrent;

namespace K3SManager.Kubernetes.Internal;

/// <summary>
/// A small single-flight cache. A dashboard that refreshes every few seconds should not turn
/// into a list storm against the API server, and one in-flight fetch per key is enough.
/// </summary>
internal sealed class TimedCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;
    private bool _disposed;

    public TimedCache(TimeSpan ttl) => _ttl = ttl;

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ttl <= TimeSpan.Zero)
        {
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        var entry = _entries.GetOrAdd(key, static _ => new Entry());

        if (entry.TryRead<T>(_ttl, out var cached))
        {
            return cached;
        }

        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (entry.TryRead<T>(_ttl, out cached))
            {
                return cached;
            }

            var value = await factory(cancellationToken).ConfigureAwait(false);
            entry.Write(value);
            return value;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    /// <summary>Called after every mutation so the next read reflects what just changed.</summary>
    public void Clear() => _entries.Clear();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _entries.Values)
        {
            entry.Gate.Dispose();
        }

        _entries.Clear();
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        private object? _value;
        private DateTimeOffset _stamp;

        public bool TryRead<T>(TimeSpan ttl, out T value)
        {
            var snapshot = Volatile.Read(ref _value);
            if (snapshot is T typed && DateTimeOffset.UtcNow - _stamp < ttl)
            {
                value = typed;
                return true;
            }

            value = default!;
            return false;
        }

        public void Write(object? value)
        {
            _stamp = DateTimeOffset.UtcNow;
            Volatile.Write(ref _value, value);
        }
    }
}
