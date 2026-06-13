namespace KeyValueStore.Server;

/// <summary>
/// Internal value wrapper with optional TTL. Only <see cref="InMemoryStore"/> creates instances.
/// </summary>
internal class StoreEntry
{
    public string Value { get; }
    public DateTime? ExpiresAt { get; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    public StoreEntry(string value, DateTime? expiresAt = null)
    {
        Value = value;
        ExpiresAt = expiresAt;
    }
}
