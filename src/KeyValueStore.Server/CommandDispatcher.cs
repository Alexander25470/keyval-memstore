using System.Runtime.CompilerServices;
using KeyValueStore.Server.Commands;
using KeyValueStore.Server.Networking;
using KeyValueStore.Server.PubSub;
using KeyValueStore.Server.Resp;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Server;

public class CommandDispatcher
{
    public delegate ValueTask Handler(string[] args, RespWriter writer, ClientSession? subscriber);

    private readonly Dictionary<string, Handler> _handlers;

    public CommandDispatcher(InMemoryStore store, PubSubHub hub, IReplicationCoordinator? replication = null)
    {
        _handlers = CreateHandlers(store, hub, replication);
    }

    public ValueTask ExecuteAsync(string[] args, RespWriter writer, ClientSession? subscriber = null)
    {
        if (args.Length == 0)
            return writer.WriteError("empty command");

        var command = args[0].ToUpperInvariant();
        if (!_handlers.TryGetValue(command, out var handler))
            return writer.WriteError($"unknown command '{args[0]}'");

        return handler(args, writer, subscriber);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Dictionary<string, Handler> CreateHandlers(InMemoryStore store, PubSubHub hub, IReplicationCoordinator? replication)
    {
        var dict = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- Server ----
            ["PING"]      = (a, w, _) => ServerCommands.Ping(a, w),
            ["ECHO"]      = (a, w, _) => ServerCommands.Echo(a, w),
            ["QUIT"]      = (_, w, _) => ServerCommands.Quit(w),

            // ---- Key ----
            ["DEL"]       = (a, w, _) => KeyCommands.Del(a, store, w, replication),
            ["EXISTS"]    = (a, w, _) => KeyCommands.Exists(a, store, w),
            ["KEYS"]      = (a, w, _) => KeyCommands.Keys(a, store, w),
            ["DBSIZE"]    = (_, w, _) => KeyCommands.DbSize(store, w),
            ["FLUSHALL"]  = (_, w, _) => KeyCommands.FlushAll(store, w, replication),
            ["EXPIRE"]    = (a, w, _) => KeyCommands.Expire(a, store, w, replication),
            ["TTL"]       = (a, w, _) => KeyCommands.Ttl(a, store, w),
            ["TYPE"]      = (a, w, _) => KeyCommands.Type(a, store, w),

            // ---- String ----
            ["SET"]       = (a, w, _) => StringCommands.Set(a, store, w, replication),
            ["GET"]       = (a, w, _) => StringCommands.Get(a, store, w),
            ["INCR"]      = (a, w, _) => StringCommands.Incr(a, store, w, replication),
            ["DECR"]      = (a, w, _) => StringCommands.Decr(a, store, w, replication),

            // ---- Set ----
            ["SADD"]      = (a, w, _) => SetCommands.SAdd(a, store, w, replication),
            ["SREM"]      = (a, w, _) => SetCommands.SRem(a, store, w, replication),
            ["SMEMBERS"]  = (a, w, _) => SetCommands.SMembers(a, store, w),
            ["SISMEMBER"] = (a, w, _) => SetCommands.SIsMember(a, store, w),
            ["SCARD"]     = (a, w, _) => SetCommands.SCard(a, store, w),

            // ---- Hash ----
            ["HSET"]      = (a, w, _) => HashCommands.HSet(a, store, w, replication),
            ["HGET"]      = (a, w, _) => HashCommands.HGet(a, store, w),
            ["HDEL"]      = (a, w, _) => HashCommands.HDel(a, store, w, replication),
            ["HGETALL"]   = (a, w, _) => HashCommands.HGetAll(a, store, w),
            ["HEXISTS"]   = (a, w, _) => HashCommands.HExists(a, store, w),
            ["HLEN"]      = (a, w, _) => HashCommands.HLen(a, store, w),
        };

        dict["PUBLISH"]     = (a, w, _) => PubSubCommands.Publish(a, hub, w);
        dict["SUBSCRIBE"]   = (a, w, s) => PubSubCommands.Subscribe(a, w, hub, s);
        dict["UNSUBSCRIBE"] = (a, w, s) => PubSubCommands.Unsubscribe(a, w, hub, s);
        dict["PSUBSCRIBE"]  = (a, w, s) => PubSubCommands.PSubscribe(a, w, hub, s);
        dict["PUNSUBSCRIBE"]= (a, w, s) => PubSubCommands.PUnsubscribe(a, w, hub, s);

        return dict;
    }
}
