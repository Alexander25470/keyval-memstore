using System.Text;
using KeyValueStore.Server;
using KeyValueStore.Server.PubSub;
using KeyValueStore.Server.Resp;

namespace KeyValueStore.Tests;

public class RespWriterTests
{
    private readonly MemoryStream _stream = new();
    private readonly RespWriter _writer;

    public RespWriterTests()
    {
        _writer = new RespWriter(_stream);
    }

    private static ReadOnlyMemory<byte> B(string s) => Encoding.ASCII.GetBytes(s);

    private async Task<string> ReadResponse()
    {
        _stream.Position = 0;
        var buf = new byte[_stream.Length];
        await _stream.ReadAsync(buf, 0, buf.Length);
        _stream.SetLength(0);
        return Encoding.UTF8.GetString(buf);
    }

    [Fact]
    public async Task WriteSimpleString_OK()
    {
        await _writer.WriteSimpleString("OK");
        Assert.Equal("+OK\r\n", await ReadResponse());
    }

    [Fact]
    public async Task WriteError_UnknownCommand()
    {
        await _writer.WriteError("unknown command 'FOO'");
        Assert.Equal("-ERR unknown command 'FOO'\r\n", await ReadResponse());
    }

    [Fact]
    public async Task WriteError_AlreadyHasErrPrefix()
    {
        await _writer.WriteError("ERR something");
        Assert.Equal("-ERR something\r\n", await ReadResponse());
    }

    [Fact]
    public async Task WriteInteger_Positive() { await _writer.WriteInteger(42); Assert.Equal(":42\r\n", await ReadResponse()); }
    [Fact]
    public async Task WriteInteger_Negative() { await _writer.WriteInteger(-1); Assert.Equal(":-1\r\n", await ReadResponse()); }
    [Fact]
    public async Task WriteInteger_Zero() { await _writer.WriteInteger(0); Assert.Equal(":0\r\n", await ReadResponse()); }

    [Fact]
    public async Task WriteBulkString_Value() { await _writer.WriteBulkString(B("bar")); Assert.Equal("$3\r\nbar\r\n", await ReadResponse()); }
    [Fact]
    public async Task WriteBulkString_Null() { await _writer.WriteBulkString(null); Assert.Equal("$-1\r\n", await ReadResponse()); }
    [Fact]
    public async Task WriteBulkString_Empty() { await _writer.WriteBulkString(B("")); Assert.Equal("$0\r\n\r\n", await ReadResponse()); }

    [Fact]
    public async Task WriteArray_TwoItems() { await _writer.WriteArray([B("a"), B("bb")]); Assert.Equal("*2\r\n$1\r\na\r\n$2\r\nbb\r\n", await ReadResponse()); }
    [Fact]
    public async Task WriteArray_Empty() { await _writer.WriteArray(Array.Empty<ReadOnlyMemory<byte>>()); Assert.Equal("*0\r\n", await ReadResponse()); }

    [Fact] public async Task WriteOk() { await _writer.WriteOk(); Assert.Equal("+OK\r\n", await ReadResponse()); }
    [Fact] public async Task WritePong() { await _writer.WritePong(); Assert.Equal("+PONG\r\n", await ReadResponse()); }
    [Fact] public async Task WriteTypeString() { await _writer.WriteTypeString(); Assert.Equal("+string\r\n", await ReadResponse()); }
    [Fact] public async Task WriteTypeNone() { await _writer.WriteTypeNone(); Assert.Equal("+none\r\n", await ReadResponse()); }

    [Fact]
    public async Task MultipleWrites_NoBufferCorruption()
    {
        await _writer.WriteOk(); Assert.Equal("+OK\r\n", await ReadResponse());
        await _writer.WriteInteger(99); Assert.Equal(":99\r\n", await ReadResponse());
        await _writer.WriteBulkString(B("x")); Assert.Equal("$1\r\nx\r\n", await ReadResponse());
    }

    // ---- pub/sub push messages ----

    [Fact]
    public async Task WritePush_Message()
    {
        var msg = new PubSubMessage("message", "orders", null, B("new!").ToArray());
        await _writer.WritePush(msg);
        Assert.Equal("*3\r\n$7\r\nmessage\r\n$6\r\norders\r\n$4\r\nnew!\r\n", await ReadResponse());
    }

    [Fact]
    public async Task WritePush_PMessage()
    {
        var msg = new PubSubMessage("pmessage", "orders.123", "orders.*", B("data").ToArray());
        await _writer.WritePush(msg);
        Assert.Equal("*4\r\n$8\r\npmessage\r\n$10\r\norders.123\r\n$8\r\norders.*\r\n$4\r\ndata\r\n", await ReadResponse());
    }

    [Fact]
    public async Task WritePush_EmptyData()
    {
        var msg = new PubSubMessage("message", "ch", null, B("").ToArray());
        await _writer.WritePush(msg);
        Assert.Equal("*3\r\n$7\r\nmessage\r\n$2\r\nch\r\n$0\r\n\r\n", await ReadResponse());
    }

    [Fact]
    public async Task WriteSubscribeAck()
    {
        await _writer.WriteSubscribeAck("subscribe", "orders", 2);
        Assert.Equal("*3\r\n$9\r\nsubscribe\r\n$6\r\norders\r\n:2\r\n", await ReadResponse());
    }

    [Fact]
    public async Task WriteSubscribeAck_Psubscribe()
    {
        await _writer.WriteSubscribeAck("psubscribe", "orders.*", 1);
        Assert.Equal("*3\r\n$10\r\npsubscribe\r\n$8\r\norders.*\r\n:1\r\n", await ReadResponse());
    }
}
