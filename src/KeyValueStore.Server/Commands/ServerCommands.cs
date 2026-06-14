using KeyValueStore.Server.Exceptions;
using KeyValueStore.Server.Resp;

namespace KeyValueStore.Server.Commands;

public static class ServerCommands
{
    public static async ValueTask Ping(string[] args, RespWriter writer)
    {
        if (args.Length > 2) { await writer.WriteError("wrong number of arguments for 'PING' command"); return; }
        if (args.Length == 2)
            await writer.WriteBulkString(args[1]);
        else
            await writer.WritePong();
    }

    public static async ValueTask Echo(string[] args, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'ECHO' command"); return; }
        await writer.WriteBulkString(args[1]);
    }

    public static async ValueTask Quit(RespWriter writer)
    {
        await writer.WriteOk();
        throw new QuitException();
    }

    /// <summary>
    /// Handles HELLO command from clients trying to negotiate RESP3.
    /// Returns a RESP2-compatible response that tells the client
    /// (e.g. StackExchange.Redis) to use RESP2 protocol (proto=2).
    /// </summary>
    public static async ValueTask Hello(RespWriter writer)
    {
        // Return a RESP2 array of key-value pairs, same format Redis 6+ returns
        // to RESP2 clients. The critical entry is "proto" = "2".
        await writer.WriteArray([
            "server", "kvstore",
            "version", "0.1.0",
            "proto", "2",
            "mode", "standalone",
            "role", "master",
        ]);
    }

    /// <summary>
    /// Minimal CLIENT command support (SETNAME, SETINFO, ID).
    /// StackExchange.Redis sends these during connection handshake.
    /// </summary>
    public static async ValueTask Client(string[] args, RespWriter writer)
    {
        if (args.Length < 2)
        {
            await writer.WriteError("wrong number of arguments for 'CLIENT' command");
            return;
        }

        var sub = args[1].ToUpperInvariant();
        switch (sub)
        {
            case "SETNAME":
                await writer.WriteOk();
                break;
            case "SETINFO":
                // CLIENT SETINFO lib-name/lib-ver — just acknowledge.
                await writer.WriteOk();
                break;
            case "ID":
                // Return a synthetic client ID.
                await writer.WriteInteger(42);
                break;
            default:
                await writer.WriteError($"unknown subcommand 'CLIENT {args[1]}'");
                break;
        }
    }
}
