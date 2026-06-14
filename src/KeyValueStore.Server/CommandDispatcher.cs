using System.Runtime.CompilerServices;

namespace KeyValueStore.Server;

public class CommandDispatcher
{
    public delegate ValueTask Handler(string[] args, RespWriter writer);

    private readonly Dictionary<string, Handler> _handlers;

    public CommandDispatcher(InMemoryStore store, IReplicationCoordinator? replication = null)
    {
        _handlers = CreateHandlers(store, replication);
    }

    public ValueTask ExecuteAsync(string[] args, RespWriter writer)
    {
        if (args.Length == 0)
            return writer.WriteError("empty command");

        var command = args[0].ToUpperInvariant();
        if (!_handlers.TryGetValue(command, out var handler))
            return writer.WriteError($"unknown command '{args[0]}'");

        return handler(args, writer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Dictionary<string, Handler> CreateHandlers(InMemoryStore store, IReplicationCoordinator? replication)
    {
        return new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- Server ----
            ["PING"]      = ServerCommands.Ping,
            ["ECHO"]      = ServerCommands.Echo,
            ["QUIT"]      = (_, w) => ServerCommands.Quit(w),

            // ---- Key ----
            ["DEL"]       = (a, w) => KeyCommands.Del(a, store, w, replication),
            ["EXISTS"]    = (a, w) => KeyCommands.Exists(a, store, w),
            ["KEYS"]      = (a, w) => KeyCommands.Keys(a, store, w),
            ["DBSIZE"]    = (_, w) => KeyCommands.DbSize(store, w),
            ["FLUSHALL"]  = (_, w) => KeyCommands.FlushAll(store, w, replication),
            ["EXPIRE"]    = (a, w) => KeyCommands.Expire(a, store, w, replication),
            ["TTL"]       = (a, w) => KeyCommands.Ttl(a, store, w),
            ["TYPE"]      = (a, w) => KeyCommands.Type(a, store, w),

            // ---- String ----
            ["SET"]       = (a, w) => StringCommands.Set(a, store, w, replication),
            ["GET"]       = (a, w) => StringCommands.Get(a, store, w),
            ["INCR"]      = (a, w) => StringCommands.Incr(a, store, w, replication),
            ["DECR"]      = (a, w) => StringCommands.Decr(a, store, w, replication),

            // ---- Set ----
            ["SADD"]      = (a, w) => SetCommands.SAdd(a, store, w, replication),
            ["SREM"]      = (a, w) => SetCommands.SRem(a, store, w, replication),
            ["SMEMBERS"]  = (a, w) => SetCommands.SMembers(a, store, w),
            ["SISMEMBER"] = (a, w) => SetCommands.SIsMember(a, store, w),
            ["SCARD"]     = (a, w) => SetCommands.SCard(a, store, w),

            // ---- Hash ----
            ["HSET"]      = (a, w) => HashCommands.HSet(a, store, w, replication),
            ["HGET"]      = (a, w) => HashCommands.HGet(a, store, w),
            ["HDEL"]      = (a, w) => HashCommands.HDel(a, store, w, replication),
            ["HGETALL"]   = (a, w) => HashCommands.HGetAll(a, store, w),
            ["HEXISTS"]   = (a, w) => HashCommands.HExists(a, store, w),
            ["HLEN"]      = (a, w) => HashCommands.HLen(a, store, w),
        };
    }
}
