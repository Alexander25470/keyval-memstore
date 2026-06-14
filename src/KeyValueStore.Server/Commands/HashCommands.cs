using System.Linq;
using System.Text;
using KeyValueStore.Server.Resp;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Server.Commands;

public static class HashCommands
{
    private static string Str(ReadOnlyMemory<byte> b) => Encoding.ASCII.GetString(b.Span);

    public static async ValueTask HSet(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 4) { await writer.WriteError("wrong number of arguments for 'HSET' command"); return; }
        int count = store.HSet(Str(args[1]), args[2], args[3]);
        if (count > 0) replication?.OnWrite("HSET", Str(args[1]), $"{Str(args[2])}={Str(args[3])}", null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask HGet(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'HGET' command"); return; }
        var val = store.HGet(Str(args[1]), args[2]);
        if (val is null) { await writer.WriteBulkString(null); return; }
        await writer.WriteBulkString(val);
    }

    public static async ValueTask HDel(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 3) { await writer.WriteError("wrong number of arguments for 'HDEL' command"); return; }
        var fields = args[2..];
        int count = store.HDel(Str(args[1]), fields);
        if (count > 0) replication?.OnWrite("HDEL", Str(args[1]), string.Join(',', fields.Select(Str)), null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask HGetAll(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'HGETALL' command"); return; }
        await writer.WriteArray(store.HGetAll(Str(args[1])).Select(m => new ReadOnlyMemory<byte>(m)).ToArray());
    }

    public static async ValueTask HExists(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'HEXISTS' command"); return; }
        await writer.WriteInteger(store.HExists(Str(args[1]), args[2]) ? 1 : 0);
    }

    public static async ValueTask HLen(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'HLEN' command"); return; }
        await writer.WriteInteger(store.HLen(Str(args[1])));
    }
}
