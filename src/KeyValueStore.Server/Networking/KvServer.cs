using System.Net;
using System.Net.Sockets;
using KeyValueStore.Server.PubSub;

namespace KeyValueStore.Server.Networking;

/// <summary>
/// TCP server that accepts client connections and spawns a
/// <see cref="ClientSession"/> per connection (fire-and-forget).
/// </summary>
public class KvServer
{
    private readonly CommandDispatcher _dispatcher;
    private readonly PubSubHub _hub;
    private readonly IPAddress _host;
    private readonly int _port;

    public KvServer(CommandDispatcher dispatcher, PubSubHub hub, string host, int port)
    {
        _dispatcher = dispatcher;
        _hub = hub;
        _host = ResolveHost(host);
        _port = port;
    }

    private static IPAddress ResolveHost(string host)
    {
        host = host.Trim();
        return host switch
        {
            "" or "*" or "+" or "0.0.0.0" => IPAddress.Any,
            "::" or "[::]" or "::0" => IPAddress.IPv6Any,
            "localhost" => IPAddress.Loopback,
            "127.0.0.1" => IPAddress.Loopback,
            _ => IPAddress.Parse(host)
        };
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
                client.NoDelay = true;  // disable Nagle — lower latency for small RESP responses
                var session = new ClientSession(client, _dispatcher, _hub);
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
