using System.Collections.Concurrent;

namespace KeyValueStore.Server;

/// <summary>
/// In-memory key-value store backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// All concurrency is contained here — callers never see locks.
/// Expired keys are removed lazily on access and actively via a background sampling loop.
/// </summary>
public class InMemoryStore
{
    private readonly ConcurrentDictionary<string, StoreEntry> _store = new();
    private static readonly Random _rng = new();

    // ---- write ----

    public void Set(string key, string value, TimeSpan? ttl = null)
    {
        DateTime? expiresAt = ttl.HasValue ? DateTime.UtcNow + ttl.Value : null;
        _store[key] = new StoreEntry(value, expiresAt);
    }

    // ---- read ----

    public string? Get(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return null;
        if (entry.IsExpired)
        {
            _store.TryRemove(key, out _);
            return null;
        }
        return entry.Value;
    }

    // ---- delete ----

    public int Delete(params string[] keys)
    {
        int count = 0;
        foreach (var key in keys)
        {
            if (_store.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                _store.TryRemove(key, out _);
                count++;
            }
        }
        return count;
    }

    // ---- exists ----

    public int Exists(params string[] keys)
    {
        int count = 0;
        foreach (var key in keys)
        {
            if (_store.TryGetValue(key, out var entry) && !entry.IsExpired)
                count++;
        }
        return count;
    }

    // ---- keys (glob) ----

    public IReadOnlyList<string> Keys(string pattern)
    {
        // Remove expired entries we stumble upon while enumerating.
        var matches = new List<string>();
        foreach (var kv in _store)
        {
            if (kv.Value.IsExpired)
            {
                _store.TryRemove(kv.Key, out _);
                continue;
            }
            if (GlobMatch(kv.Key, pattern))
                matches.Add(kv.Key);
        }
        return matches;
    }

    // ---- dbsize / flush ----

    public int DBSize()
    {
        int count = 0;
        foreach (var kv in _store)
        {
            if (kv.Value.IsExpired)
                _store.TryRemove(kv.Key, out _);
            else
                count++;
        }
        return count;
    }

    public void FlushAll() => _store.Clear();

    // ---- ttl ----

    public bool Expire(string key, int seconds)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired)
            return false;
        _store[key] = new StoreEntry(entry.Value, DateTime.UtcNow.AddSeconds(seconds));
        return true;
    }

    public long Ttl(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return -2; // key does not exist
        if (entry.IsExpired)
        {
            _store.TryRemove(key, out _);
            return -2;
        }
        if (entry.ExpiresAt is null)
            return -1; // no TTL set
        return (long)(entry.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds;
    }

    // ---- incr / decr (atomic) ----

    public long Incr(string key)
    {
        return AtomicIncrement(key, delta: 1);
    }

    public long Decr(string key)
    {
        return AtomicIncrement(key, delta: -1);
    }

    private long AtomicIncrement(string key, long delta)
    {
        // AddOrUpdate ensures read+write is atomic (no race between clients).
        var entry = _store.AddOrUpdate(
            key,
            _ => new StoreEntry(delta.ToString()),
            (_, existing) =>
            {
                if (existing.IsExpired)
                    return new StoreEntry(delta.ToString());
                if (!long.TryParse(existing.Value, out var current))
                    throw new InvalidOperationException("value is not an integer or out of range");
                return new StoreEntry((current + delta).ToString(), existing.ExpiresAt);
            });
        return long.Parse(entry.Value);
    }

    // ---- active expiration (Redis-style sampling) ----

    /// <summary>
    /// Background loop that samples random keys every 100ms and evicts expired ones.
    /// If >25% of the sample is expired, it repeats immediately (the store has many stale keys).
    /// </summary>
    public async Task RunExpirationLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var keys = _store.Keys.ToArray();
            if (keys.Length == 0)
            {
                await Task.Delay(100, ct);
                continue;
            }

            int expired = 0;
            int sampleSize = Math.Min(keys.Length, 20);

            for (int i = 0; i < sampleSize; i++)
            {
                var key = keys[_rng.Next(keys.Length)];
                if (_store.TryGetValue(key, out var entry) && entry.IsExpired)
                {
                    _store.TryRemove(key, out _);
                    expired++;
                }
            }

            // Only wait if fewer than 25% of the sample were expired.
            if (expired < sampleSize / 4)
                await Task.Delay(100, ct);
        }
    }

    // ---- glob matching (simple: * and ? only) ----

    private static bool GlobMatch(string input, string pattern)
    {
        int i = 0, p = 0;
        int starIdx = -1, matchIdx = 0;

        while (i < input.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                starIdx = p;
                matchIdx = i;
                p++;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == input[i]))
            {
                i++;
                p++;
            }
            else if (starIdx != -1)
            {
                p = starIdx + 1;
                matchIdx++;
                i = matchIdx;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }
}
