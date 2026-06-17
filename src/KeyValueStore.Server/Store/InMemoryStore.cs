using System.Runtime.InteropServices;
using System.Text;

namespace KeyValueStore.Server.Store;

public sealed class InMemoryStore : IDisposable
{
    private Node?[] _buckets;
    private int _bucketCount;
    private int _count;
    private Lock[] _locks;
    private int _resizing;
    private bool _disposed;

    private const uint Prime1 = 0x9E3779B1;
    private const int InitialBuckets = 1024;
    private const float LoadFactor = 0.75f;

    public InMemoryStore()
    {
        _bucketCount = InitialBuckets;
        _buckets = new Node?[InitialBuckets];
        _locks = new Lock[InitialBuckets];
        for (int i = 0; i < InitialBuckets; i++)
            _locks[i] = new Lock();
    }

    // ─── Public API ─────────────────────────────────────────

    public void Set(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null)
    {
        var span = key.Span;
        uint hash = Hash(span);
        var newEntry = StoreEntry.FromString(value.ToArray(),
            ttl.HasValue ? DateTime.UtcNow + ttl.Value : null);
        EnsureCapacity();
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node != null) { node.Value = newEntry; return; }
            Insert(bucket, span, hash, newEntry);
        }
    }

    public byte[]? Get(ReadOnlyMemory<byte> key)
    {
        var span = key.Span;
        uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null) return null;
            if (node.Value.IsExpired)
            {
                unsafe { Remove(bucket, span, hash); }
                return null;
            }
            return node.Value.Type == StoreType.String ? (byte[])node.Value.Value : null;
        }
    }

    public int Delete(params ReadOnlyMemory<byte>[] keys)
    {
        int count = 0;
        foreach (var k in keys)
        {
            var span = k.Span; uint hash = Hash(span);
            int bucket = (int)(hash & (_bucketCount - 1));
            lock (_locks[bucket])
            {
                Node? node;
                unsafe { node = Find(_buckets[bucket], span, hash); }
                if (node != null && !node.Value.IsExpired)
                {
                    unsafe { Remove(bucket, span, hash); }
                    count++;
                }
            }
        }
        return count;
    }

    public int Exists(params ReadOnlyMemory<byte>[] keys)
    {
        int count = 0;
        foreach (var k in keys)
        {
            var span = k.Span; uint hash = Hash(span);
            int bucket = (int)(hash & (_bucketCount - 1));
            lock (_locks[bucket])
            {
                Node? node;
                unsafe { node = Find(_buckets[bucket], span, hash); }
                if (node != null && !node.Value.IsExpired) count++;
            }
        }
        return count;
    }

    public byte[][] Keys(ReadOnlyMemory<byte> pattern)
    {
        var matches = new List<byte[]>();
        var pat = pattern.Span;
        for (int b = 0; b < _bucketCount; b++)
        {
            lock (_locks[b]) PruneExpired(b);
        }
        for (int b = 0; b < _bucketCount; b++)
        {
            lock (_locks[b])
            {
                for (var n = _buckets[b]; n != null; n = n.Next)
                {
                    unsafe
                    {
                        if (Glob.Match(new ReadOnlySpan<byte>(n.Key, n.KeyLength), pat))
                            matches.Add(CopyKey(n));
                    }
                }
            }
        }
        return matches.ToArray();
    }

    public int DBSize()
    {
        int count = 0;
        for (int b = 0; b < _bucketCount; b++)
        {
            lock (_locks[b])
            {
                PruneExpired(b);
                for (var n = _buckets[b]; n != null; n = n.Next) count++;
            }
        }
        return count;
    }

    public void FlushAll()
    {
        for (int b = 0; b < _bucketCount; b++)
        {
            lock (_locks[b])
            {
                var n = _buckets[b]; _buckets[b] = null;
                while (n != null)
                {
                    var next = n.Next;
                    unsafe { NativeMemory.Free(n.Key); }
                    n = next;
                }
            }
        }
        _count = 0;
    }

    public string Type(ReadOnlyMemory<byte> key)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired) return "none";
            return node.Value.Type switch
            {
                StoreType.String => "string", StoreType.Set => "set",
                StoreType.Hash => "hash", _ => "none"
            };
        }
    }

    public bool Expire(ReadOnlyMemory<byte> key, int seconds)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired) return false;
            node.Value = node.Value.WithExpiry(DateTime.UtcNow.AddSeconds(seconds));
            return true;
        }
    }

    public long Ttl(ReadOnlyMemory<byte> key)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null) return -2;
            if (node.Value.IsExpired)
            {
                unsafe { Remove(bucket, span, hash); }
                return -2;
            }
            if (node.Value.ExpiresAt is null) return -1;
            return (long)(node.Value.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds;
        }
    }

    public long Incr(ReadOnlyMemory<byte> key) => AtomicIncrement(key, 1);
    public long Decr(ReadOnlyMemory<byte> key) => AtomicIncrement(key, -1);

    private long AtomicIncrement(ReadOnlyMemory<byte> key, long delta)
    {
        var span = key.Span; uint hash = Hash(span);
        EnsureCapacity();
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired)
            {
                Insert(bucket, span, hash, StoreEntry.FromString(ToBytes(delta)));
                return delta;
            }
            if (node.Value.Type != StoreType.String)
                throw new InvalidOperationException("value is not an integer or out of range");
            var bytes = (byte[])node.Value.Value;
            if (!TryParseLong(bytes, out var current))
                throw new InvalidOperationException("value is not an integer or out of range");
            node.Value = StoreEntry.FromString(ToBytes(current + delta), node.Value.ExpiresAt);
            return current + delta;
        }
    }

    // ─── Sets ────────────────────────────────────────────────

    public int SAdd(ReadOnlyMemory<byte> key, params ReadOnlyMemory<byte>[] members)
    {
        int added = 0;
        var span = key.Span; uint hash = Hash(span);
        EnsureCapacity();
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            HashSet<byte[]> set; DateTime? expiresAt;
            if (node == null || node.Value.IsExpired)
            {
                set = new HashSet<byte[]>(ByteArrayComparer.Instance);
                expiresAt = null;
            }
            else if (node.Value.Type == StoreType.Set)
            {
                set = (HashSet<byte[]>)node.Value.Value;
                expiresAt = node.Value.ExpiresAt;
            }
            else return 0;
            foreach (var m in members) if (set.Add(m.ToArray())) added++;
            var entry = StoreEntry.FromSet(set, expiresAt);
            if (node == null || node.Value.IsExpired)
                Insert(bucket, span, hash, entry);
            else node.Value = entry;
        }
        return added;
    }

    public int SRem(ReadOnlyMemory<byte> key, params ReadOnlyMemory<byte>[] members)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Set) return 0;
            var set = (HashSet<byte[]>)node.Value.Value; int removed = 0;
            foreach (var m in members) if (set.Remove(m.ToArray())) removed++;
            return removed;
        }
    }

    public byte[][] SMembers(ReadOnlyMemory<byte> key)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Set)
                return Array.Empty<byte[]>();
            return ((HashSet<byte[]>)node.Value.Value).ToArray();
        }
    }

    public bool SIsMember(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> member)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Set) return false;
            return ((HashSet<byte[]>)node.Value.Value).Contains(member.ToArray());
        }
    }

    public int SCard(ReadOnlyMemory<byte> key)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Set) return 0;
            return ((HashSet<byte[]>)node.Value.Value).Count;
        }
    }

    // ─── Hashes ──────────────────────────────────────────────

    public int HSet(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> field, ReadOnlyMemory<byte> value)
    {
        var span = key.Span; uint hash = Hash(span);
        EnsureCapacity();
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            Dictionary<byte[], byte[]> dict; DateTime? expiresAt;
            if (node == null || node.Value.IsExpired)
            {
                dict = new Dictionary<byte[], byte[]>(ByteArrayComparer.Instance);
                expiresAt = null;
            }
            else if (node.Value.Type == StoreType.Hash)
            {
                dict = (Dictionary<byte[], byte[]>)node.Value.Value;
                expiresAt = node.Value.ExpiresAt;
            }
            else return 0;
            dict[field.ToArray()] = value.ToArray();
            var entry = StoreEntry.FromHash(dict, expiresAt);
            if (node == null || node.Value.IsExpired)
                Insert(bucket, span, hash, entry);
            else node.Value = entry;
        }
        return 1;
    }

    public byte[]? HGet(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> field)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Hash) return null;
            return ((Dictionary<byte[], byte[]>)node.Value.Value).TryGetValue(field.ToArray(), out var val) ? val : null;
        }
    }

    public int HDel(ReadOnlyMemory<byte> key, params ReadOnlyMemory<byte>[] fields)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Hash) return 0;
            var dict = (Dictionary<byte[], byte[]>)node.Value.Value; int removed = 0;
            foreach (var f in fields) if (dict.Remove(f.ToArray())) removed++;
            return removed;
        }
    }

    public byte[][] HGetAll(ReadOnlyMemory<byte> key)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Hash)
                return Array.Empty<byte[]>();
            var dict = (Dictionary<byte[], byte[]>)node.Value.Value;
            var result = new List<byte[]>(dict.Count * 2);
            foreach (var kv in dict) { result.Add(kv.Key); result.Add(kv.Value); }
            return result.ToArray();
        }
    }

    public bool HExists(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> field)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Hash) return false;
            return ((Dictionary<byte[], byte[]>)node.Value.Value).ContainsKey(field.ToArray());
        }
    }

    public int HLen(ReadOnlyMemory<byte> key)
    {
        var span = key.Span; uint hash = Hash(span);
        int bucket = (int)(hash & (_bucketCount - 1));
        lock (_locks[bucket])
        {
            Node? node;
            unsafe { node = Find(_buckets[bucket], span, hash); }
            if (node == null || node.Value.IsExpired || node.Value.Type != StoreType.Hash) return 0;
            return ((Dictionary<byte[], byte[]>)node.Value.Value).Count;
        }
    }

    // ─── Active expiration ───────────────────────────────────

    public async Task RunExpirationLoop(CancellationToken ct)
    {
        var rng = new Random();
        while (!ct.IsCancellationRequested)
        {
            var keys = CollectActiveKeys();
            if (keys.Count == 0) { await Task.Delay(100, ct); continue; }
            int expired = 0;
            int sampleSize = Math.Min(keys.Count, 20);
            for (int i = 0; i < sampleSize; i++)
            {
                var (ptr, keyLen) = keys[rng.Next(keys.Count)];
                ReadOnlySpan<byte> span;
                unsafe { span = new ReadOnlySpan<byte>((byte*)ptr, keyLen); }
                uint hash = Hash(span);
                int bucket = (int)(hash & (_bucketCount - 1));
                lock (_locks[bucket])
                {
                    Node? node;
                    unsafe { node = Find(_buckets[bucket], span, hash); }
                    if (node != null && node.Value.IsExpired)
                    {
                        unsafe { Remove(bucket, span, hash); }
                        expired++;
                    }
                }
            }
            if (expired < sampleSize / 4) await Task.Delay(100, ct);
        }
    }

    public void Dispose()
    {
        if (!_disposed) { _disposed = true; FlushAll(); }
    }

    // ═════════════════════════════════════════════════════════
    //  Internals
    // ═════════════════════════════════════════════════════════

    private sealed unsafe class Node
    {
        public Node? Next;
        public uint Hash;
        public int KeyLength;
        public byte* Key;
        public StoreEntry Value;
    }

    private static unsafe Node? Find(Node? head, ReadOnlySpan<byte> key, uint hash)
    {
        for (var n = head; n != null; n = n.Next)
        {
            if (n.Hash != hash) continue;
            if (key.SequenceEqual(new ReadOnlySpan<byte>(n.Key, n.KeyLength)))
                return n;
        }
        return null;
    }

    private void Insert(int bucket, ReadOnlySpan<byte> key, uint hash, StoreEntry value)
    {
        unsafe
        {
            var node = new Node
            {
                Key = (byte*)NativeMemory.Alloc((nuint)key.Length),
                KeyLength = key.Length,
                Hash = hash,
                Value = value,
                Next = _buckets[bucket]
            };
            key.CopyTo(new Span<byte>(node.Key, key.Length));
            _buckets[bucket] = node;
        }
        _count++;
    }

    private void EnsureCapacity()
    {
        if (_count < (int)(_bucketCount * LoadFactor)) return;
        // Only one thread performs the resize; others spin-wait.
        if (Interlocked.CompareExchange(ref _resizing, 1, 0) == 0)
        {
            try { if (_count >= (int)(_bucketCount * LoadFactor)) Grow(); }
            finally { Volatile.Write(ref _resizing, 0); }
        }
        else
        {
            var sw = new SpinWait();
            while (Volatile.Read(ref _resizing) == 1) sw.SpinOnce();
        }
    }

    private unsafe void Remove(int bucket, ReadOnlySpan<byte> key, uint hash)
    {
        Node? prev = null;
        for (var n = _buckets[bucket]; n != null; n = n.Next)
        {
            if (n.Hash != hash || !key.SequenceEqual(new ReadOnlySpan<byte>(n.Key, n.KeyLength)))
            {
                prev = n; continue;
            }
            if (prev == null) _buckets[bucket] = n.Next;
            else prev.Next = n.Next;
            NativeMemory.Free(n.Key);
            _count--;
            return;
        }
    }

    private void PruneExpired(int bucket)
    {
        Node? prev = null;
        for (var n = _buckets[bucket]; n != null;)
        {
            if (n.Value.IsExpired)
            {
                if (prev == null) _buckets[bucket] = n.Next;
                else prev.Next = n.Next;
                var dead = n; n = n.Next;
                unsafe { NativeMemory.Free(dead.Key); }
                _count--;
            }
            else { prev = n; n = n.Next; }
        }
    }

    private void Grow()
    {
        int oldCount = _bucketCount;
        int newBucketCount = oldCount * 2;
        for (int i = 0; i < oldCount; i++) _locks[i].Enter();
        try
        {
            var newBuckets = new Node?[newBucketCount];
            for (int b = 0; b < oldCount; b++)
            {
                var n = _buckets[b];
                while (n != null)
                {
                    var next = n.Next;
                    int nb = (int)(n.Hash & (newBucketCount - 1));
                    n.Next = newBuckets[nb];
                    newBuckets[nb] = n;
                    n = next;
                }
            }
            _buckets = newBuckets;
            var newLocks = new Lock[newBucketCount];
            Array.Copy(_locks, newLocks, oldCount);
            for (int i = oldCount; i < newBucketCount; i++) newLocks[i] = new Lock();
            _locks = newLocks;
            _bucketCount = newBucketCount;
        }
        finally { for (int i = 0; i < oldCount; i++) _locks[i].Exit(); }
    }

    private static unsafe byte[] CopyKey(Node n)
    {
        var key = new byte[n.KeyLength];
        new ReadOnlySpan<byte>(n.Key, n.KeyLength).CopyTo(key);
        return key;
    }

    private List<(IntPtr Key, int KeyLen)> CollectActiveKeys()
    {
        var keys = new List<(IntPtr, int)>(_count);
        for (int b = 0; b < _bucketCount; b++)
        {
            lock (_locks[b])
            {
                for (var n = _buckets[b]; n != null; n = n.Next)
                {
                    if (!n.Value.IsExpired)
                    {
                        unsafe { keys.Add(((IntPtr)n.Key, n.KeyLength)); }
                    }
                }
            }
        }
        return keys;
    }

    private static uint Hash(ReadOnlySpan<byte> data)
    {
        uint h = 0;
        foreach (byte b in data) h = ((h << 5) + h + b) ^ Prime1;
        return h;
    }

    private static byte[] ToBytes(long v) => Encoding.ASCII.GetBytes(v.ToString());
    private static bool TryParseLong(byte[] b, out long v) => long.TryParse(Encoding.ASCII.GetString(b), out v);
}
