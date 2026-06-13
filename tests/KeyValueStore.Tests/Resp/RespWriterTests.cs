using System.Text;
using KeyValueStore.Server;

namespace KeyValueStore.Tests;

public class RespWriterTests
{
    private readonly MemoryStream _stream = new();
    private readonly RespWriter _writer;

    public RespWriterTests()
    {
        _writer = new RespWriter(_stream);
    }

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
    public async Task WriteBulkString_Value() { await _writer.WriteBulkString("bar"); Assert.Equal("$3\r\nbar\r\n", await ReadResponse()); }
    [Fact]
    public async Task WriteBulkString_Null() { await _writer.WriteBulkString(null); Assert.Equal("$-1\r\n", await ReadResponse()); }
    [Fact]
    public async Task WriteBulkString_Empty() { await _writer.WriteBulkString(""); Assert.Equal("$0\r\n\r\n", await ReadResponse()); }

    [Fact]
    public async Task WriteArray_TwoItems() { await _writer.WriteArray(["a", "bb"]); Assert.Equal("*2\r\n$1\r\na\r\n$2\r\nbb\r\n", await ReadResponse()); }
    [Fact]
    public async Task WriteArray_Empty() { await _writer.WriteArray(Array.Empty<string>()); Assert.Equal("*0\r\n", await ReadResponse()); }

    [Fact] public async Task WriteOk() { await _writer.WriteOk(); Assert.Equal("+OK\r\n", await ReadResponse()); }
    [Fact] public async Task WritePong() { await _writer.WritePong(); Assert.Equal("+PONG\r\n", await ReadResponse()); }
    [Fact] public async Task WriteTypeString() { await _writer.WriteTypeString(); Assert.Equal("+string\r\n", await ReadResponse()); }
    [Fact] public async Task WriteTypeNone() { await _writer.WriteTypeNone(); Assert.Equal("+none\r\n", await ReadResponse()); }

    [Fact]
    public async Task MultipleWrites_NoBufferCorruption()
    {
        await _writer.WriteOk(); Assert.Equal("+OK\r\n", await ReadResponse());
        await _writer.WriteInteger(99); Assert.Equal(":99\r\n", await ReadResponse());
        await _writer.WriteBulkString("x"); Assert.Equal("$1\r\nx\r\n", await ReadResponse());
    }
}
