namespace KeyValueStore.Server;

/// <summary>
/// Dispatches parsed RESP commands to their handlers. Each handler is a delegate
/// that receives parsed arguments, the shared store, and a writer for the response.
/// Validates arity before invoking. Phase 2 hook: accepts an optional
/// <see cref="IReplicationCoordinator"/> (null in Phase 1).
/// </summary>
public class CommandDispatcher
{
    /// <summary>
    /// A command handler receives the parsed arguments (args[0] is the command name)
    /// and the response writer. The store is captured via closure at registration time.
    /// </summary>
    public delegate ValueTask Handler(string[] args, RespWriter writer);

    private readonly IReadOnlyDictionary<string, Handler> _handlers;
    private readonly IReplicationCoordinator? _replication;

    public CommandDispatcher(InMemoryStore store, IReplicationCoordinator? replication = null)
    {
        _replication = replication;
        _handlers = CreateHandlers(store);
    }

    /// <summary>
    /// Routes a parsed command to its handler. Returns a task that writes the response.
    /// </summary>
    public ValueTask ExecuteAsync(string[] args, RespWriter writer)
    {
        if (args.Length == 0)
        {
            return writer.WriteError("empty command");
        }

        var command = args[0].ToUpperInvariant();
        if (!_handlers.TryGetValue(command, out var handler))
        {
            return writer.WriteError($"unknown command '{args[0]}'");
        }

        return handler(args, writer);
    }

    // ---- handler registration ----

    private Dictionary<string, Handler> CreateHandlers(InMemoryStore store)
    {
        var h = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase);

        // ---- server commands ----
        h["PING"]     = (args, w) => HandlePing(args, w);
        h["ECHO"]     = (args, w) => HandleEcho(args, w);
        h["QUIT"]     = (_, w)   => HandleQuit(w);

        // ---- key commands ----
        h["DEL"]      = (args, w) => HandleDel(args, store, w);
        h["EXISTS"]   = (args, w) => HandleExists(args, store, w);
        h["KEYS"]     = (args, w) => HandleKeys(args, store, w);
        h["DBSIZE"]   = (_, w)    => HandleDbSize(store, w);
        h["FLUSHALL"] = (_, w)    => HandleFlushAll(store, w);
        h["EXPIRE"]   = (args, w) => HandleExpire(args, store, w);
        h["TTL"]      = (args, w) => HandleTtl(args, store, w);
        h["TYPE"]     = (args, w) => HandleType(args, store, w);

        // ---- string commands ----
        h["SET"]      = (args, w) => HandleSet(args, store, w);
        h["GET"]      = (args, w) => HandleGet(args, store, w);
        h["INCR"]     = (args, w) => HandleIncr(args, store, w);
        h["DECR"]     = (args, w) => HandleDecr(args, store, w);

        return h;
    }

    // ---- server commands ----

    private static async ValueTask HandlePing(string[] args, RespWriter writer)
    {
        if (args.Length > 2) { await writer.WriteError("wrong number of arguments for 'PING' command"); return; }
        if (args.Length == 2)
            await writer.WriteBulkString(args[1]);
        else
            await writer.WritePong();
    }

    private static async ValueTask HandleEcho(string[] args, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'ECHO' command"); return; }
        await writer.WriteBulkString(args[1]);
    }

    private static async ValueTask HandleQuit(RespWriter writer)
    {
        await writer.WriteOk();
        throw new QuitException();
    }

    // ---- key commands ----

    private static async ValueTask HandleDel(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'DEL' command"); return; }
        int count = store.Delete(args[1..]);
        await writer.WriteInteger(count);
    }

    private static async ValueTask HandleExists(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'EXISTS' command"); return; }
        int count = store.Exists(args[1..]);
        await writer.WriteInteger(count);
    }

    private static async ValueTask HandleKeys(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'KEYS' command"); return; }
        var keys = store.Keys(args[1]);
        await writer.WriteArray(keys);
    }

    private static async ValueTask HandleDbSize(InMemoryStore store, RespWriter writer)
    {
        await writer.WriteInteger(store.DBSize());
    }

    private static async ValueTask HandleFlushAll(InMemoryStore store, RespWriter writer)
    {
        store.FlushAll();
        await writer.WriteOk();
    }

    private static async ValueTask HandleExpire(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'EXPIRE' command"); return; }
        if (!int.TryParse(args[2], out var seconds) || seconds < 0)
        {
            await writer.WriteError("value is not an integer or out of range");
            return;
        }
        bool ok = store.Expire(args[1], seconds);
        await writer.WriteInteger(ok ? 1 : 0);
    }

    private static async ValueTask HandleTtl(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'TTL' command"); return; }
        await writer.WriteInteger(store.Ttl(args[1]));
    }

    private static async ValueTask HandleType(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'TYPE' command"); return; }
        var value = store.Get(args[1]);
        await (value is null ? writer.WriteTypeNone() : writer.WriteTypeString());
    }

    // ---- string commands ----

    private async ValueTask HandleSet(string[] args, InMemoryStore store, RespWriter writer)
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
            else
            {
                await writer.WriteError("syntax error");
                return;
            }
        }

        store.Set(args[1], args[2], ttl);
        _replication?.OnWrite("SET", args[1], args[2], ttl);
        await writer.WriteOk();
    }

    private static async ValueTask HandleGet(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'GET' command"); return; }
        var value = store.Get(args[1]);
        await writer.WriteBulkString(value);
    }

    private static async ValueTask HandleIncr(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'INCR' command"); return; }
        try { await writer.WriteInteger(store.Incr(args[1])); }
        catch (InvalidOperationException) { await writer.WriteError("value is not an integer or out of range"); }
    }

    private static async ValueTask HandleDecr(string[] args, InMemoryStore store, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'DECR' command"); return; }
        try { await writer.WriteInteger(store.Decr(args[1])); }
        catch (InvalidOperationException) { await writer.WriteError("value is not an integer or out of range"); }
    }
}

/// <summary>
/// Phase 2 hook — replication coordinator interface. Null-object in Phase 1.
/// </summary>
public interface IReplicationCoordinator
{
    void OnWrite(string command, string key, string value, TimeSpan? ttl);
}

/// <summary>
/// Thrown by the QUIT handler to signal the client session loop to exit cleanly.
/// </summary>
public class QuitException : Exception { }
