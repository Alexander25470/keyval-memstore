using System.Linq;
using System.Text;
using KeyValueStore.Server.Exceptions;
using KeyValueStore.Server.Resp;

namespace KeyValueStore.Server.Commands;

public static class ServerCommands
{
    private static readonly ReadOnlyMemory<byte>[] HelloResponse = CreateHelloResponse();

    private static ReadOnlyMemory<byte>[] CreateHelloResponse()
    {
        static ReadOnlyMemory<byte> B(string s) => Encoding.ASCII.GetBytes(s);
        return [B("server"), B("kvstore"), B("version"), B("0.1.0"),
                B("proto"), B("2"), B("mode"), B("standalone"),
                B("role"), B("master")];
    }

    private static string Str(ReadOnlyMemory<byte> b) => Encoding.ASCII.GetString(b.Span);

    public static async ValueTask Ping(ReadOnlyMemory<byte>[] args, RespWriter writer)
    {
        if (args.Length > 2) { await writer.WriteError("wrong number of arguments for 'PING' command"); return; }
        if (args.Length == 2)
            await writer.WriteBulkString(args[1]);
        else
            await writer.WritePong();
    }

    public static async ValueTask Echo(ReadOnlyMemory<byte>[] args, RespWriter writer)
    {
        if (args.Length != 2) { await writer.WriteError("wrong number of arguments for 'ECHO' command"); return; }
        await writer.WriteBulkString(args[1]);
    }

    public static async ValueTask Quit(RespWriter writer)
    {
        await writer.WriteOk();
        throw new QuitException();
    }

    public static async ValueTask Hello(RespWriter writer)
    {
        await writer.WriteArray(HelloResponse);
    }

    public static async ValueTask Client(ReadOnlyMemory<byte>[] args, RespWriter writer)
    {
        if (args.Length < 2)
        {
            await writer.WriteError("wrong number of arguments for 'CLIENT' command");
            return;
        }

        var sub = Str(args[1]).ToUpperInvariant();
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
