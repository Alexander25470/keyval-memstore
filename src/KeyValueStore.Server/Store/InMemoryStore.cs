using System.Collections.Concurrent;
using System.Text;

namespace KeyValueStore.Server.Store;

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

    public void Set(string key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null)
    {
        DateTime? expiresAt = ttl.HasValue ? DateTime.UtcNow + ttl.Value : null;
        _store[key] = StoreEntry.FromString(value.ToArray(), expiresAt);
    }

    // ---- read ----

    public byte[]? Get(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return null;
        if (entry.IsExpired)
        {
            _store.TryRemove(key, out _);
            return null;
        }
        return entry.Type == StoreType.String ? (byte[])entry.Value : null;
    }

    // ---- type ----

    public string Type(string key)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired)
            return "none";
        return entry.Type switch
        {
            StoreType.String => "string",
            StoreType.Set => "set",
            StoreType.Hash => "hash",
            _ => "none"
        };
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
            if (Glob.Match(kv.Key, pattern))
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
        _store[key] = entry.WithExpiry(DateTime.UtcNow.AddSeconds(seconds));
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
        var entry = _store.AddOrUpdate(
            key,
            _ => StoreEntry.FromString(ToBytes(delta)),
            (_, existing) =>
            {
                if (existing.IsExpired)
                    return StoreEntry.FromString(ToBytes(delta));
                if (existing.Type != StoreType.String)
                    throw new InvalidOperationException("value is not an integer or out of range");
                var val = (byte[])existing.Value;
                if (!TryParseLong(val, out var current))
                    throw new InvalidOperationException("value is not an integer or out of range");
                return StoreEntry.FromString(ToBytes(current + delta), existing.ExpiresAt);
            });
        return ParseLong((byte[])entry.Value);
    }

    private static byte[] ToBytes(long value) => Encoding.ASCII.GetBytes(value.ToString());
    private static bool TryParseLong(byte[] bytes, out long value) => long.TryParse(Encoding.ASCII.GetString(bytes), out value);
    private static long ParseLong(byte[] bytes) => long.Parse(Encoding.ASCII.GetString(bytes));

    // ---- sets ----

    public int SAdd(string key, params ReadOnlyMemory<byte>[] members)
    {
        int added = 0;
        _store.AddOrUpdate(
            key,
            _ =>
            {
                var s = new HashSet<byte[]>(ByteArrayComparer.Instance);
                foreach (var m in members) if (s.Add(m.ToArray())) added++;
                return StoreEntry.FromSet(s);
            },
            (_, existing) =>
            {
                if (existing.IsExpired) { var s = new HashSet<byte[]>(ByteArrayComparer.Instance); foreach (var m in members) if (s.Add(m.ToArray())) added++; return StoreEntry.FromSet(s); }
                if (existing.Type != StoreType.Set) return existing;
                var set = (HashSet<byte[]>)existing.Value;
                foreach (var m in members) if (set.Add(m.ToArray())) added++;
                return StoreEntry.FromSet(set, existing.ExpiresAt);
            });
        return added;
    }

    public int SRem(string key, params ReadOnlyMemory<byte>[] members)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Set)
            return 0;
        var set = (HashSet<byte[]>)entry.Value;
        int removed = 0;
        foreach (var m in members) if (set.Remove(m.ToArray())) removed++;
        return removed;
    }

    public byte[][] SMembers(string key)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Set)
            return Array.Empty<byte[]>();
        return ((HashSet<byte[]>)entry.Value).ToArray();
    }

    public bool SIsMember(string key, ReadOnlyMemory<byte> member)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Set)
            return false;
        return ((HashSet<byte[]>)entry.Value).Contains(member.ToArray());
    }

    public int SCard(string key)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Set)
            return 0;
        return ((HashSet<byte[]>)entry.Value).Count;
    }

    // ---- hashes ----

    public int HSet(string key, ReadOnlyMemory<byte> field, ReadOnlyMemory<byte> value)
    {
        var entry = _store.AddOrUpdate(
            key,
            _ => StoreEntry.FromHash(new Dictionary<byte[], byte[]>(ByteArrayComparer.Instance) { [field.ToArray()] = value.ToArray() }),
            (_, existing) =>
            {
                if (existing.IsExpired) return StoreEntry.FromHash(new Dictionary<byte[], byte[]>(ByteArrayComparer.Instance) { [field.ToArray()] = value.ToArray() });
                if (existing.Type != StoreType.Hash) return existing;
                var hash = (Dictionary<byte[], byte[]>)existing.Value;
                hash[field.ToArray()] = value.ToArray();
                return StoreEntry.FromHash(hash, existing.ExpiresAt);
            });
        return entry.Type == StoreType.Hash ? 1 : 0;
    }

    public byte[]? HGet(string key, ReadOnlyMemory<byte> field)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Hash)
            return null;
        return ((Dictionary<byte[], byte[]>)entry.Value).TryGetValue(field.ToArray(), out var val) ? val : null;
    }

    public int HDel(string key, params ReadOnlyMemory<byte>[] fields)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Hash)
            return 0;
        var hash = (Dictionary<byte[], byte[]>)entry.Value;
        int removed = 0;
        foreach (var f in fields) if (hash.Remove(f.ToArray())) removed++;
        return removed;
    }

    public byte[][] HGetAll(string key)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Hash)
            return Array.Empty<byte[]>();
        var hash = (Dictionary<byte[], byte[]>)entry.Value;
        var result = new List<byte[]>();
        foreach (var kv in hash) { result.Add(kv.Key); result.Add(kv.Value); }
        return result.ToArray();
    }

    public bool HExists(string key, ReadOnlyMemory<byte> field)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Hash)
            return false;
        return ((Dictionary<byte[], byte[]>)entry.Value).ContainsKey(field.ToArray());
    }

    public int HLen(string key)
    {
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired || entry.Type != StoreType.Hash)
            return 0;
        return ((Dictionary<byte[], byte[]>)entry.Value).Count;
    }

    // ---- active expiration (Redis-style sampling) ----
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
}
