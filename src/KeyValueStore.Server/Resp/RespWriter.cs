using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace KeyValueStore.Server;

/// <summary>
/// Writes RESP2 responses directly to a <see cref="Stream"/> using an
/// <see cref="ArrayBufferWriter{Byte}"/> to avoid intermediate string allocations.
/// </summary>
public class RespWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly ArrayBufferWriter<byte> _buf = new(256);

    // Pre-encoded byte sequences.
    private static readonly byte[] CRLF = "\r\n"u8.ToArray();
    private static readonly byte[] DollarMinusOne = "$-1\r\n"u8.ToArray();
    private static readonly byte[] ErrPrefix = "-ERR "u8.ToArray();
    private static readonly byte[] TypeString = "+string\r\n"u8.ToArray();
    private static readonly byte[] TypeNone = "+none\r\n"u8.ToArray();

    public RespWriter(Stream stream) => _stream = stream;

    // ---- public write methods ----

    public ValueTask WriteSimpleString(string value)
    {
        _buf.Clear();
        WriteByte((byte)'+');
        WriteUtf8(value);
        WriteBytes(CRLF);
        return FlushAsync();
    }

    public ValueTask WriteError(string message)
    {
        _buf.Clear();
        if (!message.StartsWith("ERR", StringComparison.Ordinal))
            WriteBytes(ErrPrefix);
        else
            WriteByte((byte)'-');
        WriteUtf8(message);
        WriteBytes(CRLF);
        return FlushAsync();
    }

    public ValueTask WriteInteger(long value)
    {
        _buf.Clear();
        WriteByte((byte)':');
        WriteInt64(value);
        WriteBytes(CRLF);
        return FlushAsync();
    }

    public ValueTask WriteBulkString(string? value)
    {
        _buf.Clear();
        if (value is null)
        {
            WriteBytes(DollarMinusOne);
            return FlushAsync();
        }
        WriteByte((byte)'$');
        WriteInt64(value.Length);
        WriteBytes(CRLF);
        WriteUtf8(value);
        WriteBytes(CRLF);
        return FlushAsync();
    }

    public ValueTask WriteArray(IReadOnlyList<string> items)
    {
        _buf.Clear();
        WriteByte((byte)'*');
        WriteInt64(items.Count);
        WriteBytes(CRLF);
        foreach (var item in items)
        {
            WriteByte((byte)'$');
            WriteInt64(item.Length);
            WriteBytes(CRLF);
            WriteUtf8(item);
            WriteBytes(CRLF);
        }
        return FlushAsync();
    }

    public ValueTask WriteTypeString() => WriteRaw(TypeString);
    public ValueTask WriteTypeNone()   => WriteRaw(TypeNone);
    public ValueTask WriteOk()         => WriteRaw("+OK\r\n"u8);
    public ValueTask WritePong()       => WriteRaw("+PONG\r\n"u8);

    // ---- helpers ----

    private ValueTask WriteRaw(ReadOnlySpan<byte> data)
    {
        _buf.Clear();
        WriteBytes(data);
        return FlushAsync();
    }

    private ValueTask FlushAsync()
    {
        return _stream.WriteAsync(_buf.WrittenMemory);
    }

    private void WriteByte(byte b)
    {
        var span = _buf.GetSpan(1);
        span[0] = b;
        _buf.Advance(1);
    }

    private void WriteBytes(ReadOnlySpan<byte> data)
    {
        _buf.Write(data);
    }

    private void WriteUtf8(string s)
    {
        var span = _buf.GetSpan(Encoding.UTF8.GetMaxByteCount(s.Length));
        int written = Encoding.UTF8.GetBytes(s, span);
        _buf.Advance(written);
    }

    private void WriteInt64(long value)
    {
        if (value < 0)
        {
            WriteByte((byte)'-');
            value = -value;
        }
        if (value == 0)
        {
            WriteByte((byte)'0');
            return;
        }
        int digits = CountDigits(value);
        var span = _buf.GetSpan(digits);
        for (int i = digits - 1; i >= 0; i--)
        {
            span[i] = (byte)('0' + (value % 10));
            value /= 10;
        }
        _buf.Advance(digits);
    }

    private static int CountDigits(long value)
    {
        int d = 0;
        while (value > 0) { d++; value /= 10; }
        return d;
    }

    public void Dispose() { }
}
