namespace KeyValueStore.Server.Store;

internal enum StoreType { String, Set, Hash }

/// <summary>
/// Internal value wrapper with optional TTL. Holds one of:
/// <c>byte[]</c>, <c>HashSet&lt;byte[]&gt;</c>, or <c>Dictionary&lt;byte[], byte[]&gt;</c>.
/// All byte arrays are owned copies — safe to store and mutate independently.
/// </summary>
internal class StoreEntry
{
    public object Value { get; }
    public StoreType Type { get; }
    public DateTime? ExpiresAt { get; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    private StoreEntry(object value, StoreType type, DateTime? expiresAt)
    {
        Value = value;
        Type = type;
        ExpiresAt = expiresAt;
    }

    public static StoreEntry FromString(byte[] value, DateTime? expiresAt = null)
        => new(value, StoreType.String, expiresAt);

    public static StoreEntry FromSet(HashSet<byte[]> set, DateTime? expiresAt = null)
        => new(set, StoreType.Set, expiresAt);

    public static StoreEntry FromHash(Dictionary<byte[], byte[]> hash, DateTime? expiresAt = null)
        => new(hash, StoreType.Hash, expiresAt);

    public StoreEntry WithExpiry(DateTime expiresAt) => new(Value, Type, expiresAt);
}
