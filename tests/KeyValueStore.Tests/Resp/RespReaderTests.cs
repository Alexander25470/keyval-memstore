using System.Text;
using KeyValueStore.Server;
using KeyValueStore.Server.Exceptions;
using KeyValueStore.Server.Resp;

namespace KeyValueStore.Tests;

public class RespReaderTests
{
    private readonly RespReader _reader = new();

    private static MemoryStream MakeStream(string data)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(data));
    }

    private static void AssertCmd(ReadOnlyMemory<byte>[]? actual, params string[] expected)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], Encoding.ASCII.GetString(actual[i].Span));
    }

    [Fact]
    public async Task Parse_SET_Command()
    {
        using var ms = MakeStream("*3\r\n$3\r\nSET\r\n$3\r\nfoo\r\n$3\r\nbar\r\n");
        var r = await _reader.ReadCommand(ms);
        AssertCmd(r, "SET", "foo", "bar");
    }

    [Fact]
    public async Task Parse_GET_Command()
    {
        using var ms = MakeStream("*2\r\n$3\r\nGET\r\n$3\r\nfoo\r\n");
        var r = await _reader.ReadCommand(ms);
        AssertCmd(r, "GET", "foo");
    }

    [Fact]
    public async Task Parse_PING_Array()
    {
        using var ms = MakeStream("*1\r\n$4\r\nPING\r\n");
        var r = await _reader.ReadCommand(ms);
        Assert.NotNull(r);
        AssertCmd(r, "PING");
    }

    [Fact]
    public async Task Parse_DEL_MultipleKeys()
    {
        using var ms = MakeStream("*3\r\n$3\r\nDEL\r\n$1\r\na\r\n$1\r\nb\r\n");
        var r = await _reader.ReadCommand(ms);
        Assert.NotNull(r);
        AssertCmd(r, "DEL", "a", "b");
    }

    [Fact]
    public async Task Parse_Inline_PING()
    {
        using var ms = MakeStream("PING\r\n");
        var r = await _reader.ReadCommand(ms);
        Assert.NotNull(r);
        AssertCmd(r, "PING");
    }

    [Fact]
    public async Task Parse_Inline_SET()
    {
        using var ms = MakeStream("SET foo bar\r\n");
        var r = await _reader.ReadCommand(ms);
        Assert.NotNull(r);
        AssertCmd(r, "SET", "foo", "bar");
    }

    [Fact]
    public async Task ClosedStream_ReturnsNull()
    {
        using var ms = MakeStream("");
        var r = await _reader.ReadCommand(ms);
        Assert.Null(r);
    }

    [Fact]
    public async Task EmptyBulkString_ParsedFine()
    {
        using var ms = MakeStream("*2\r\n$3\r\nSET\r\n$0\r\n\r\n");
        var r = await _reader.ReadCommand(ms);
        Assert.NotNull(r);
        AssertCmd(r, "SET", "");
    }

    [Fact]
    public async Task Malformed_ArrayWithInvalidCount_Throws()
    {
        using var ms = MakeStream("*abc\r\n");
        await Assert.ThrowsAsync<ProtocolException>(() => _reader.ReadCommand(ms).AsTask());
    }

    [Fact]
    public async Task RoundTrip_Array()
    {
        using var ms = new MemoryStream();
        var writer = new RespWriter(ms);
        await writer.WriteArray(new[] { "SET", "key", "value" }.Select(s => new ReadOnlyMemory<byte>(Encoding.ASCII.GetBytes(s))).ToArray());

        ms.Position = 0;
        var result = await _reader.ReadCommand(ms);
        AssertCmd(result, "SET", "key", "value");
    }
}

