using System.Buffers;
using System.Net.Sockets;
using System.Text;
using KeyValueStore.Server.PubSub;

namespace KeyValueStore.Server.Resp;

/// <summary>
/// Writes RESP2 responses directly to a <see cref="Stream"/> using an
/// <see cref="ArrayBufferWriter{Byte}"/> to avoid intermediate string allocations.
/// </summary>
public class RespWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly ArrayBufferWriter<byte> _buf = new(256);

    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

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
        WriteLatin1(value);
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
        WriteLatin1(message);
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

        // Encode to Latin-1 for binary-safe round-trips.
        // Latin-1 maps bytes 0-255 1:1 to Unicode code points.
        int byteCount = value.Length; // Latin-1: 1 char = 1 byte
        WriteByte((byte)'$');
        WriteInt64(byteCount);
        WriteBytes(CRLF);
        WriteLatin1(value);
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
            WriteLatin1(item);
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

    private void WriteLatin1(string s)
    {
        var span = _buf.GetSpan(s.Length);
        for (int i = 0; i < s.Length; i++)
            span[i] = (byte)s[i];
        _buf.Advance(s.Length);
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

    // ---- pub/sub push messages ----

    /// <summary>Write a push notification (array of bulk strings).</summary>
    public ValueTask WritePush(PubSubMessage msg)
    {
        _buf.Clear();
        // message → *3, pmessage → *4
        int count = msg.Pattern is null ? 3 : 4;
        WriteByte((byte)'*');
        WriteInt64(count);
        WriteBytes(CRLF);

        WriteBulkStringInline(msg.Type);
        WriteBulkStringInline(msg.Channel);
        if (msg.Pattern is not null)
            WriteBulkStringInline(msg.Pattern);
        WriteBulkStringInline(msg.Data);

        return FlushAsync();
    }

    /// <summary>Write a subscription confirmation e.g. *3\r\n$9\r\nsubscribe\r\n$5\r\nfoo\r\n:1\r\n</summary>
    public ValueTask WriteSubscribeAck(string type, string channel, int count)
    {
        _buf.Clear();
        WriteByte((byte)'*');
        WriteInt64(3);
        WriteBytes(CRLF);
        WriteBulkStringInline(type);
        WriteBulkStringInline(channel);
        WriteByte((byte)':');
        WriteInt64(count);
        WriteBytes(CRLF);
        return FlushAsync();
    }

    private void WriteBulkStringInline(string value)
    {
        int byteCount = value.Length; // Latin-1: 1 char = 1 byte
        WriteByte((byte)'$');
        WriteInt64(byteCount);
        WriteBytes(CRLF);
        WriteLatin1(value);
        WriteBytes(CRLF);
    }

    public void Dispose() { }
}
