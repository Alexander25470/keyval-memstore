namespace KeyValueStore.Server;

/// <summary>Phase 2 hook — replication coordinator. Null-object in Phase 1.</summary>
public interface IReplicationCoordinator
{
    void OnWrite(string command, string key, string value, TimeSpan? ttl);
}
