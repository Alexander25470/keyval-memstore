namespace KeyValueStore.Server;

public static class SetCommands
{
    public static async ValueTask SAdd(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 3) { await writer.WriteError("wrong number of arguments for 'SADD' command"); return; }
        int count = store.SAdd(args[1], args[2..]);
        if (count > 0) replication?.OnWrite("SADD", args[1], string.Join(',', args[2..]), null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask SRem(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 3) { await writer.WriteError("wrong number of arguments for 'SREM' command"); return; }
        int count = store.SRem(args[1], args[2..]);
        if (count > 0) replication?.OnWrite("SREM", args[1], string.Join(',', args[2..]), null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask SMembers(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'SMEMBERS' command"); return; }
        await writer.WriteArray(store.SMembers(args[1]));
    }

    public static async ValueTask SIsMember(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'SISMEMBER' command"); return; }
        await writer.WriteInteger(store.SIsMember(args[1], args[2]) ? 1 : 0);
    }

    public static async ValueTask SCard(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'SCARD' command"); return; }
        await writer.WriteInteger(store.SCard(args[1]));
    }
}
