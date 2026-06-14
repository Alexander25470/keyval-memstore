using System.Linq;
using System.Text;
using KeyValueStore.Server.Resp;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Server.Commands;

public static class KeyCommands
{
    private static string Str(ReadOnlyMemory<byte> b) => Encoding.ASCII.GetString(b.Span);

    public static async ValueTask Del(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'DEL' command"); return; }
        var keys = args[1..].Select(Str).ToArray();
        int count = store.Delete(keys);
        if (count > 0) replication?.OnWrite("DEL", string.Join(',', keys), "", null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask Exists(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'EXISTS' command"); return; }
        await writer.WriteInteger(store.Exists(args[1..].Select(Str).ToArray()));
    }

    public static async ValueTask Keys(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'KEYS' command"); return; }
        var matches = store.Keys(Str(args[1]));
        await writer.WriteArray(matches.Select(s => (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(s)).ToArray());
    }

    public static async ValueTask DbSize(InMemoryStore store, RespWriter writer)
        => await writer.WriteInteger(store.DBSize());

    public static async ValueTask FlushAll(InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        store.FlushAll();
        replication?.OnWrite("FLUSHALL", "", "", null);
        await writer.WriteOk();
    }

    public static async ValueTask Expire(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'EXPIRE' command"); return; }
        if (!int.TryParse(Str(args[2]), out var seconds) || seconds < 0)
        {
            await writer.WriteError("value is not an integer or out of range");
            return;
        }
        bool result = store.Expire(Str(args[1]), seconds);
        if (result) replication?.OnWrite("EXPIRE", Str(args[1]), seconds.ToString(), null);
        await writer.WriteInteger(result ? 1 : 0);
    }

    public static async ValueTask Ttl(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'TTL' command"); return; }
        await writer.WriteInteger(store.Ttl(Str(args[1])));
    }

    public static async ValueTask PTtl(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'PTTL' command"); return; }
        var ttl = store.Ttl(Str(args[1]));
        await writer.WriteInteger(ttl >= 0 ? ttl * 1000L : ttl);
    }

    public static async ValueTask Type(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'TYPE' command"); return; }
        await writer.WriteSimpleString(store.Type(Str(args[1])));
    }
}
