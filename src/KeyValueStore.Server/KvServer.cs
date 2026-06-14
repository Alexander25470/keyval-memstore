using System.Net;
using System.Net.Sockets;

namespace KeyValueStore.Server;

/// <summary>
/// TCP server that accepts client connections and spawns a
/// <see cref="ClientSession"/> per connection (fire-and-forget).
/// </summary>
public class KvServer
{
    private readonly CommandDispatcher _dispatcher;
    private readonly IPAddress _host;
    private readonly int _port;

    public KvServer(CommandDispatcher dispatcher, string host, int port)
    {
        _dispatcher = dispatcher;
        _host = IPAddress.Parse(host);
        _port = port;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(_host, _port);
        listener.Start();

        Console.Error.WriteLine($"Listening on {_host}:{_port}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                var session = new ClientSession(client, _dispatcher);
                _ = session.RunAsync(ct); // fire-and-forget
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
            // Brief grace period for in-flight sessions.
            await Task.Delay(2000, CancellationToken.None);
            Console.Error.WriteLine("Server stopped.");
        }
    }
}
