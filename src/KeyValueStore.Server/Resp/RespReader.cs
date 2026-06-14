using System.Buffers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using KeyValueStore.Server.Exceptions;

namespace KeyValueStore.Server.Resp;

/// <summary>
/// Parses RESP2 commands from a <see cref="NetworkStream"/>.
/// Supports both RESP arrays (e.g. *2\r\n$3\r\nGET\r\n...) and inline commands
/// (e.g. PING\r\n) for telnet/netcat compatibility.
/// </summary>
public class RespReader : IDisposable
{
    private readonly byte[] _buffer;
    private int _pos;
    private int _total;

    public RespReader(int bufferSize = 4096)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
    }

    /// <summary>
    /// Reads one complete command from the stream.
    /// Returns <c>null</c> when the client disconnects cleanly (zero bytes read).
    /// Handles pipelined data: leftover bytes from a previous read are consumed
    /// before reading more from the stream.
    /// </summary>
    public async ValueTask<ReadOnlyMemory<byte>[]?> ReadCommand(Stream stream)
    {
        // If we have leftover data from a previous read, parse the next command from it.
        if (_pos >= _total)
        {
            _pos = 0;
            _total = await stream.ReadAsync(_buffer, 0, _buffer.Length);
            if (_total == 0)
                return null;
        }

        return _buffer[_pos] switch
        {
            (byte)'*' => await ReadArray(stream),
            _ => ParseInlineRemaining()
        };
    }

    /// <summary>Parse an inline command from remaining buffer data, consuming up to CRLF.</summary>
    private ReadOnlyMemory<byte>[] ParseInlineRemaining()
    {
        int start = _pos;
        while (_pos < _total && !(_pos + 1 < _total && _buffer[_pos] == '\r' && _buffer[_pos + 1] == '\n'))
            _pos++;
        if (_pos + 1 >= _total)
            throw NewProtocol("Inline command missing CRLF");

        var line = _buffer.AsMemory(start, _pos - start);
        _pos += 2;
        return SplitInlineBytes(line);
    }

    private static ReadOnlyMemory<byte>[] SplitInlineBytes(ReadOnlyMemory<byte> line)
    {
        var span = line.Span;
        int parts = 0;
        bool inWord = false;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == ' ') { inWord = false; }
            else if (!inWord) { parts++; inWord = true; }
        }

        var result = new ReadOnlyMemory<byte>[parts];
        int idx = 0, wordStart = -1;
        for (int i = 0; i <= span.Length; i++)
        {
            if (i < span.Length && span[i] != ' ')
            {
                if (wordStart < 0) wordStart = i;
            }
            else
            {
                if (wordStart >= 0)
                {
                    result[idx++] = line.Slice(wordStart, i - wordStart);
                    wordStart = -1;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Reads any single RESP value (simple string, error, integer, bulk string, or array).
    /// Returns a string array where each element is one part of the response.
    /// For simple types: [value]. For arrays: [item1, item2, ...].
    /// For null bulk strings: empty array.
    /// </summary>
    public async ValueTask<ReadOnlyMemory<byte>[]> ReadValue(Stream stream)
    {
        _total = await stream.ReadAsync(_buffer, 0, _buffer.Length);
        if (_total == 0) return Array.Empty<ReadOnlyMemory<byte>>();
        _pos = 0;

        if (_buffer[0] == (byte)'*') return await ReadArray(stream);

        // Simple string, error, integer: read line and return as single-element array.
        if (_buffer[0] is (byte)'+' or (byte)'-' or (byte)':')
        {
            var line = await ReadAnyLine(stream, 1);
            return [_buffer.AsMemory(1, line - 1)];
        }

        // Bulk string: read header + body.
        if (_buffer[0] == (byte)'$')
        {
            var val = await ReadBulkString(stream);
            return val.Length == 0 ? Array.Empty<ReadOnlyMemory<byte>>() : [val];
        }

        return Array.Empty<ReadOnlyMemory<byte>>();
    }

    /// <summary>Reads bytes until CRLF, starting from current _pos. Returns position after CRLF.</summary>
    private async Task<int> ReadAnyLine(Stream stream, int start)
    {
        _pos = start;
        while (_pos < _total && !(_pos + 1 < _total && _buffer[_pos] == '\r' && _buffer[_pos + 1] == '\n'))
            _pos++;
        if (_pos + 1 >= _total)
        {
            await ReadMore(stream, 256);
            while (_pos < _total && !(_pos + 1 < _total && _buffer[_pos] == '\r' && _buffer[_pos + 1] == '\n'))
                _pos++;
        }
        _pos += 2; // skip CRLF
        return _pos - 2; // position of CR
    }

    // ---- RESP array ----

    private async ValueTask<ReadOnlyMemory<byte>[]> ReadArray(Stream stream)
    {
        _pos++; // skip '*'
        int count = (int)ReadInt(_buffer, ref _pos, _total);
        ExpectCRLF(_buffer, ref _pos);

        var args = new ReadOnlyMemory<byte>[count];
        for (int i = 0; i < count; i++)
            args[i] = await ReadBulkString(stream);
        return args;
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadBulkString(Stream stream)
    {
        // Read header: $LEN\r\n — consume from buffer first, then stream if needed.
        int headerStart = _pos;
        while (_pos < _total && !(_pos + 1 < _total && _buffer[_pos] == '\r' && _buffer[_pos + 1] == '\n'))
            _pos++;

        if (_pos + 1 >= _total)
        {
            // CRLF not in buffer — read more from stream.
            int extra = await ReadMore(stream, 256);
            while (_pos < _total && !(_pos + 1 < _total && _buffer[_pos] == '\r' && _buffer[_pos + 1] == '\n'))
                _pos++;
            if (_pos + 1 >= _total && extra == 0)
                throw NewProtocol("Unexpected end of stream in bulk string header");
        }

        int headerEnd = _pos;
        _pos += 2; // skip CRLF

        var header = _buffer.AsSpan(headerStart, headerEnd - headerStart);
        if (header[0] != (byte)'$')
            throw NewProtocol($"Expected '$', got '{(char)header[0]}'");

        // Parse length
        int sign = 1, idx = 1;
        if (idx < header.Length && header[idx] == '-') { sign = -1; idx++; }
        int len = 0;
        while (idx < header.Length && header[idx] >= '0' && header[idx] <= '9')
            len = len * 10 + (header[idx++] - '0');
        len *= sign;

        if (len < -1) throw NewProtocol($"Negative bulk string length: {len}");
        if (len == -1) return ReadOnlyMemory<byte>.Empty;

        int needed = len + 2 - (_total - _pos);
        if (needed > 0)
            await ReadMore(stream, needed);

        if (_total - _pos < len + 2)
            throw NewProtocol("Unexpected end of stream reading bulk string data");

        var result = _buffer.AsMemory(_pos, Math.Max(0, len));
        _pos += len + 2;
        return result;
    }

    private async Task<int> ReadMore(Stream stream, int minBytes)
    {
        // Compact remaining bytes to start of buffer, then read more.
        int remaining = _total - _pos;
        if (remaining > 0 && _pos > 0)
            Array.Copy(_buffer, _pos, _buffer, 0, remaining);
        _pos = 0;
        _total = remaining;

        int space = _buffer.Length - _total;
        int toRead = Math.Min(minBytes, space);
        int read = await stream.ReadAsync(_buffer, _total, toRead);
        _total += read;
        return read;
    }

    // ---- low-level helpers ----

    private static long ReadInt(byte[] buf, ref int pos, int max)
    {
        long value = 0;
        while (pos < max && buf[pos] >= '0' && buf[pos] <= '9')
        {
            value = value * 10 + (buf[pos] - '0');
            pos++;
        }
        return value;
    }

    private static void ExpectCRLF(byte[] buf, ref int pos)
    {
        if (pos + 1 >= buf.Length || buf[pos] != '\r' || buf[pos + 1] != '\n')
            throw NewProtocol("Expected CRLF");
        pos += 2;
    }

    private static ProtocolException NewProtocol(string message)
        => new($"Protocol error: {message}");

    public void Dispose()
    {
        if (_buffer is not null)
            ArrayPool<byte>.Shared.Return(_buffer);
    }
}
