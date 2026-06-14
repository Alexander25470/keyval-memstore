using System.Buffers;

namespace KeyValueStore.Server.Store;

/// <summary>
/// Structural equality comparer for <see cref="byte[]"/>.
/// Uses <see cref="Span{T}.SequenceEqual"/> (JIT intrinsic, SIMD) for equality
/// and <see cref="HashCode.AddBytes(ReadOnlySpan{byte})"/> (accelerado en .NET 8+)
/// para hashing. Performance equivalente a <c>string</c>'s built-in comparer.
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
        var hc = new HashCode();
        hc.AddBytes(obj);
        return hc.ToHashCode();
    }
}
