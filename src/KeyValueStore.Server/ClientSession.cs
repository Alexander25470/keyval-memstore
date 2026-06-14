using System.Net.Sockets;

namespace KeyValueStore.Server;

/// <summary>
/// Handles a single client connection: reads RESP commands in a loop,
/// dispatches them, and writes responses. Catches exceptions to close cleanly.
/// </summary>
public class ClientSession
{
    private readonly TcpClient _client;
    private readonly CommandDispatcher _dispatcher;
    private readonly RespReader _reader = new();
    private RespWriter? _writer;

    public ClientSession(TcpClient client, CommandDispatcher dispatcher)
    {
        _client = client;
        _dispatcher = dispatcher;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var stream = _client.GetStream();
            _writer = new RespWriter(stream);

            while (!ct.IsCancellationRequested)
            {
                string[]? args;
                try
                {
                    args = await _reader.ReadCommand(stream);
                }
                catch (ProtocolException ex)
                {
                    await _writer.WriteError(ex.Message);
                    break;
                }

                if (args is null)
                    break; // client disconnected

                try
                {
                    await _dispatcher.ExecuteAsync(args, _writer);
                }
                catch (QuitException)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            // client disconnected abruptly — ignore
        }
        finally
        {
            _reader.Dispose();
            _client.Dispose();
        }
    }
}
