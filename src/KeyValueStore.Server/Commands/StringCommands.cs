using KeyValueStore.Server.Resp;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Server.Commands;

public static class StringCommands
{
    public static async ValueTask Set(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length < 3 || args.Length > 5) { await writer.WriteError("wrong number of arguments for 'SET' command"); return; }

        TimeSpan? ttl = null;
        if (args.Length >= 4)
        {
            var opt = args[3].ToUpperInvariant();
            if (opt == "EX" && args.Length == 5 && double.TryParse(args[4], out var ex))
                ttl = TimeSpan.FromSeconds(ex);
            else if (opt == "PX" && args.Length == 5 && double.TryParse(args[4], out var px))
                ttl = TimeSpan.FromMilliseconds(px);
            else { await writer.WriteError("syntax error"); return; }
        }

        store.Set(args[1], args[2], ttl);
        replication?.OnWrite("SET", args[1], args[2], ttl);
        await writer.WriteOk();
    }

    public static async ValueTask Get(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'GET' command"); return; }
        await writer.WriteBulkString(store.Get(args[1]));
    }

    public static async ValueTask Incr(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'INCR' command"); return; }
        try { var val = store.Incr(args[1]); replication?.OnWrite("INCR", args[1], val.ToString(), null); await writer.WriteInteger(val); }
        catch (InvalidOperationException) { await writer.WriteError("value is not an integer or out of range"); }
    }

    public static async ValueTask Decr(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'DECR' command"); return; }
        try { var val = store.Decr(args[1]); replication?.OnWrite("DECR", args[1], val.ToString(), null); await writer.WriteInteger(val); }
        catch (InvalidOperationException) { await writer.WriteError("value is not an integer or out of range"); }
    }

    /// <summary>SETEX key seconds value — atomic SET with EXpire.</summary>
    public static async ValueTask SetEx(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 4) { await writer.WriteError("wrong number of arguments for 'SETEX' command"); return; }
        if (!double.TryParse(args[2], out var seconds) || seconds < 0)
        {
            await writer.WriteError("value is not an integer or out of range");
            return;
        }
        var ttl = TimeSpan.FromSeconds(seconds);
        store.Set(args[1], args[3], ttl);
        replication?.OnWrite("SETEX", args[1], args[3], ttl);
        await writer.WriteOk();
    }

    /// <summary>PSETEX key milliseconds value — atomic SET with PXpire.</summary>
    public static async ValueTask PSetEx(string[] args, InMemoryStore store, RespWriter writer, IReplicationCoordinator? replication = null)
    {
        if (args.Length != 4) { await writer.WriteError("wrong number of arguments for 'PSETEX' command"); return; }
        if (!double.TryParse(args[2], out var ms) || ms < 0)
        {
            await writer.WriteError("value is not an integer or out of range");
            return;
        }
        var ttl = TimeSpan.FromMilliseconds(ms);
        store.Set(args[1], args[3], ttl);
        replication?.OnWrite("PSETEX", args[1], args[3], ttl);
        await writer.WriteOk();
    }
}
