using System.Linq;
using System.Text;
using KeyValueStore.Server.Resp;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Server.Commands;

public static class SetCommands
{
    private static string Str(ReadOnlyMemory<byte> b) => Encoding.ASCII.GetString(b.Span);

    public static async ValueTask SAdd(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 3) { await writer.WriteError("wrong number of arguments for 'SADD' command"); return; }
        var members = args[2..];
        int count = store.SAdd(Str(args[1]), members);
        if (count > 0) replication?.OnWrite("SADD", Str(args[1]), string.Join(',', members.Select(Str)), null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask SRem(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 3) { await writer.WriteError("wrong number of arguments for 'SREM' command"); return; }
        var members = args[2..];
        int count = store.SRem(Str(args[1]), members);
        if (count > 0) replication?.OnWrite("SREM", Str(args[1]), string.Join(',', members.Select(Str)), null);
        await writer.WriteInteger(count);
    }

    public static async ValueTask SMembers(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'SMEMBERS' command"); return; }
        await writer.WriteArray(store.SMembers(Str(args[1])).Select(m => new ReadOnlyMemory<byte>(m)).ToArray());
    }

    public static async ValueTask SIsMember(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'SISMEMBER' command"); return; }
        await writer.WriteInteger(store.SIsMember(Str(args[1]), args[2]) ? 1 : 0);
    }

    public static async ValueTask SCard(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'SCARD' command"); return; }
        await writer.WriteInteger(store.SCard(Str(args[1])));
    }
}
