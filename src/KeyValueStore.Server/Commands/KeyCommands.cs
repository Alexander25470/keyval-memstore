namespace KeyValueStore.Server.Commands;

using System.Linq;
using KeyValueStore.Server.Resp;
using KeyValueStore.Server.Store;

public static class KeyCommands
{
    public static async ValueTask Del(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'DEL' command"); return; }
        int count = store.Delete(args[1..]);
        if (count > 0) replication?.OnWrite("DEL", string.Join(',', args.Skip(1)), "", null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask Exists(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'EXISTS' command"); return; }
        await writer.WriteInteger(store.Exists(args[1..]));
    }

    public static async ValueTask Keys(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'KEYS' command"); return; }
        await writer.WriteArray(store.Keys(args[1]));
    }

    public static async ValueTask DbSize(InMemoryStore store, RespWriter writer)
        => await writer.WriteInteger(store.DBSize());

    public static async ValueTask FlushAll(InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        store.FlushAll();
        replication?.OnWrite("FLUSHALL", "", "", null);
        await writer.WriteOk();
    }

    public static async ValueTask Expire(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'EXPIRE' command"); return; }
        if (!int.TryParse(args[2], out var seconds) || seconds < 0)
        {
            await writer.WriteError("value is not an integer or out of range");
            return;
        }
        bool result = store.Expire(args[1], seconds);
        if (result) replication?.OnWrite("EXPIRE", args[1], seconds.ToString(), null);
        await writer.WriteInteger(result ? 1 : 0);
    }

    public static async ValueTask Ttl(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'TTL' command"); return; }
        await writer.WriteInteger(store.Ttl(args[1]));
    }

    public static async ValueTask Type(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'TYPE' command"); return; }
        await writer.WriteSimpleString(store.Type(args[1]));
    }
}
