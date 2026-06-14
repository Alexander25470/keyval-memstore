using System.Net;
using System.Net.Sockets;
using KeyValueStore.Server;
using KeyValueStore.Server.Networking;
using KeyValueStore.Server.PubSub;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Tests;

/// <summary>
/// xUnit collection fixture that starts a single <see cref="KvServer"/>
/// instance and keeps it alive for the duration of the test run.
/// All test classes that need a real TCP server share this fixture via
/// <c>IClassFixture&lt;ServerFixture&gt;</c>, avoiding 47× server startup.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _expirationTask;
    private readonly Task _serverTask;

    public int Port { get; }
    public InMemoryStore Store { get; }
    public CommandDispatcher Dispatcher { get; }

    public ServerFixture()
    {
        Port = GetRandomPort();
        Store = new InMemoryStore();
        var hub = new PubSubHub();
        Dispatcher = new CommandDispatcher(Store, hub);
        var server = new KvServer(Dispatcher, hub, "127.0.0.1", Port);
        _expirationTask = Store.RunExpirationLoop(_cts.Token);
        _serverTask = server.RunAsync(_cts.Token);
    }

    public async Task InitializeAsync()
    {
        // Wait until the server is actually listening.
        using var ctsConnect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ctsConnect.Token.IsCancellationRequested)
        {
            try
            {
                using var test = new TcpClient();
                await test.ConnectAsync(IPAddress.Loopback, Port, ctsConnect.Token);
                break;
            }
            catch
            {
                await Task.Delay(50, ctsConnect.Token);
            }
        }
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        try { await _serverTask; } catch (OperationCanceledException) { }
        try { await _expirationTask; } catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    private static int GetRandomPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
