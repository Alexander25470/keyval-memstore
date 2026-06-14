using System.Text;
using KeyValueStore.Server.Resp;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Server.Commands;

public static class StringCommands
{
    private static string Str(ReadOnlyMemory<byte> b) => Encoding.ASCII.GetString(b.Span);

    public static async ValueTask Set(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 3 || args.Length > 5) { await writer.WriteError("wrong number of arguments for 'SET' command"); return; }

        TimeSpan? ttl = null;
        if (args.Length >= 4)
        {
            var opt = Str(args[3]).ToUpperInvariant();
            if (opt == "EX" && args.Length == 5 && double.TryParse(Str(args[4]), out var ex))
                ttl = TimeSpan.FromSeconds(ex);
            else if (opt == "PX" && args.Length == 5 && double.TryParse(Str(args[4]), out var px))
                ttl = TimeSpan.FromMilliseconds(px);
            else { await writer.WriteError("syntax error"); return; }
        }

        store.Set(Str(args[1]), args[2], ttl);
        replication?.OnWrite("SET", Str(args[1]), Str(args[2]), ttl);
        await writer.WriteOk();
    }

    public static async ValueTask Get(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'GET' command"); return; }
        var val = store.Get(Str(args[1]));
        if (val is null) { await writer.WriteBulkString(null); return; }
        await writer.WriteBulkString(val);
    }

    public static async ValueTask Incr(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'INCR' command"); return; }
        try { var val = store.Incr(Str(args[1])); replication?.OnWrite("INCR", Str(args[1]), val.ToString(), null); await writer.WriteInteger(val); }
        catch (InvalidOperationException) { await writer.WriteError("value is not an integer or out of range"); }
    }

    public static async ValueTask Decr(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'DECR' command"); return; }
        try { var val = store.Decr(Str(args[1])); replication?.OnWrite("DECR", Str(args[1]), val.ToString(), null); await writer.WriteInteger(val); }
        catch (InvalidOperationException) { await writer.WriteError("value is not an integer or out of range"); }
    }

    public static async ValueTask SetEx(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 4) { await writer.WriteError("wrong number of arguments for 'SETEX' command"); return; }
        if (!double.TryParse(Str(args[2]), out var seconds) || seconds < 0)
        {
            await writer.WriteError("value is not an integer or out of range");
            return;
        }
        var ttl = TimeSpan.FromSeconds(seconds);
        store.Set(Str(args[1]), args[3], ttl);
        replication?.OnWrite("SETEX", Str(args[1]), Str(args[3]), ttl);
        await writer.WriteOk();
    }

    public static async ValueTask PSetEx(ReadOnlyMemory<byte>[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 4) { await writer.WriteError("wrong number of arguments for 'PSETEX' command"); return; }
        if (!double.TryParse(Str(args[2]), out var ms) || ms < 0)
        {
            await writer.WriteError("value is not an integer or out of range");
            return;
        }
        var ttl = TimeSpan.FromMilliseconds(ms);
        store.Set(Str(args[1]), args[3], ttl);
        replication?.OnWrite("PSETEX", Str(args[1]), Str(args[3]), ttl);
        await writer.WriteOk();
    }
}
