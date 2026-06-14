namespace KeyValueStore.Server;

using System.Linq;

public static class HashCommands
{
    public static async ValueTask HSet(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 4) { await writer.WriteError("wrong number of arguments for 'HSET' command"); return; }
        int count = store.HSet(args[1], args[2], args[3]);
        if (count > 0) replication?.OnWrite("HSET", args[1], $"{args[2]}={args[3]}", null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask HGet(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'HGET' command"); return; }
        await writer.WriteBulkString(store.HGet(args[1], args[2]));
    }

    public static async ValueTask HDel(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 3) { await writer.WriteError("wrong number of arguments for 'HDEL' command"); return; }
        int count = store.HDel(args[1], args[2..]);
        if (count > 0) replication?.OnWrite("HDEL", args[1], string.Join(',', args.Skip(2)), null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask HGetAll(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'HGETALL' command"); return; }
        await writer.WriteArray(store.HGetAll(args[1]));
    }

    public static async ValueTask HExists(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'HEXISTS' command"); return; }
        await writer.WriteInteger(store.HExists(args[1], args[2]) ? 1 : 0);
    }

    public static async ValueTask HLen(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'HLEN' command"); return; }
        await writer.WriteInteger(store.HLen(args[1]));
    }
}
