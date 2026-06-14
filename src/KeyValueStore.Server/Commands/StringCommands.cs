namespace KeyValueStore.Server;

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
}
