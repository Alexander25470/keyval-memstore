using System.Buffers;

namespace KeyValueStore.Server.Store;

/// <summary>
/// Structural equality comparer for <see cref="byte[]"/>.
/// FNV-1a hash for fast distribution; SequenceEqual for equality.
/// Used by <see cref="InMemoryStore"/> for set and hash operations
/// where byte-level key matching is required.
/// </summary>
internal sealed class ByteArrayComparer : IEqualityComparer<byte[]>
{
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() { }

    public bool Equals(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.AsSpan().SequenceEqual(y);
    }

    public int GetHashCode(byte[] obj)
    {
        // FNV-1a hash of the byte array content.
        unchecked
        {
            uint hash = 2166136261;
            foreach (var b in obj)
            {
                hash ^= b;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }
}
