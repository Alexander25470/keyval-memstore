namespace KeyValueStore.Server;

public interface IReplicationCoordinator
{
    void OnWrite(string command, string key, string value, TimeSpan? ttl);
}
