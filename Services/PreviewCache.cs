using System.Collections.Concurrent;

namespace Gelato.Services;

/// <summary>Small bounded cache with coalesced refresh, stale results, and retry backoff.</summary>
public sealed class PreviewCache<T>(int capacity = 512, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private sealed class Entry
    {
        public readonly object Gate = new();
        public T? Value;
        public bool HasValue;
        public DateTimeOffset NextRefresh;
        public Task<T>? Pending;
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _admission = new();

    public async Task<T> GetAsync(string key, TimeSpan ttl, Func<Task<T>> fetch, CancellationToken ct = default)
    {
        Entry entry;
        lock (_admission)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                if (_entries.Count >= capacity)
                {
                    var oldest = _entries.Where(e => e.Value.Pending is null).OrderBy(e => e.Value.NextRefresh).FirstOrDefault();
                    if (oldest.Key is not null) _entries.TryRemove(oldest.Key, out _);
                }
                if (_entries.Count >= capacity) throw new InvalidOperationException("Catalogue refresh capacity reached; retry shortly.");
                entry = new Entry(); _entries[key] = entry;
            }
        }
        Task<T> pending;
        lock (entry.Gate)
        {
            if (entry.HasValue && entry.NextRefresh > _time.GetUtcNow()) return entry.Value!;
            // Task.Yield in RefreshAsync ensures Pending is assigned before completion.
            pending = entry.Pending ??= RefreshAsync(entry, ttl, fetch);
            if (entry.HasValue) return entry.Value!;
        }
        return await pending.WaitAsync(ct);
    }

    private async Task<T> RefreshAsync(Entry entry, TimeSpan ttl, Func<Task<T>> fetch)
    {
        await Task.Yield();
        try
        {
            var value = await fetch();
            lock (entry.Gate)
            {
                entry.Value = value; entry.HasValue = true;
                entry.NextRefresh = _time.GetUtcNow().Add(ttl);
            }
            return value;
        }
        catch
        {
            lock (entry.Gate)
            {
                entry.NextRefresh = _time.GetUtcNow().AddSeconds(30);
                if (entry.HasValue) return entry.Value!;
            }
            throw;
        }
        finally { lock (entry.Gate) entry.Pending = null; }
    }
}
