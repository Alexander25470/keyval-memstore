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
}
