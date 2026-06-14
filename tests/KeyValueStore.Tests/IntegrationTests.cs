using System.Net;
using System.Net.Sockets;
using KeyValueStore.Server;
using KeyValueStore.Server.Networking;
using KeyValueStore.Server.PubSub;
using KeyValueStore.Server.Resp;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Tests;

/// <summary>
/// End-to-end tests: commands are encoded with <see cref="RespWriter"/> and
/// responses decoded with <see cref="RespReader.ReadValue"/> — proving the
/// server correctly speaks RESP2 in both directions.
/// </summary>
public class IntegrationTests : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly int _port;
    private readonly Task _serverTask;

    public IntegrationTests()
    {
        _port = GetRandomPort();
        var store = new InMemoryStore();
        var hub = new PubSubHub();
        var dispatcher = new CommandDispatcher(store, hub);
        var server = new KvServer(dispatcher, hub, "127.0.0.1", _port);
        _ = store.RunExpirationLoop(_cts.Token);
        _serverTask = server.RunAsync(_cts.Token);
    }

    private static int GetRandomPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Encodes a command with RespWriter, sends it, and decodes the response with RespReader.</summary>
    private async Task<string[]> Send(string[] command)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        var stream = client.GetStream();

        // Encode command as a proper RESP array.
        var writer = new RespWriter(stream);
        await writer.WriteArray(command);

        // Decode the response as any RESP value.
        var reader = new RespReader();
        return await reader.ReadValue(stream);
    }

    // ---- tests ----

    [Fact] public async Task Ping() => Assert.Equal(["PONG"], await Send(["PING"]));
    [Fact] public async Task PingMsg() => Assert.Equal(["hello"], await Send(["PING", "hello"]));
    [Fact] public async Task Echo() => Assert.Equal(["world"], await Send(["ECHO", "world"]));
    [Fact] public async Task SetOk() => Assert.Equal(["OK"], await Send(["SET", "k", "v"]));
    [Fact] public async Task GetNull() => Assert.Empty(await Send(["GET", "x"]));

    [Fact]
    public async Task SetGet_RoundTrip()
    {
        await Send(["SET", "foo", "bar"]);
        Assert.Equal(["bar"], await Send(["GET", "foo"]));
    }

    [Fact] public async Task Incr() { Assert.Equal(["1"], await Send(["INCR", "c"])); Assert.Equal(["2"], await Send(["INCR", "c"])); }
    [Fact] public async Task Decr() { await Send(["SET", "v", "10"]); Assert.Equal(["9"], await Send(["DECR", "v"])); }

    [Fact]
    public async Task Del_ReturnsCount()
    {
        await Send(["SET", "a", "1"]); await Send(["SET", "b", "2"]);
        Assert.Equal(["2"], await Send(["DEL", "a", "b", "c"]));
    }

    [Fact]
    public async Task Exists_ReturnsCount()
    {
        await Send(["SET", "x", "1"]);
        Assert.Equal(["1"], await Send(["EXISTS", "x", "y"]));
    }

    [Fact]
    public async Task Keys_ReturnsMatchingKeys()
    {
        await Send(["SET", "aa", "1"]); await Send(["SET", "ab", "2"]); await Send(["SET", "cc", "3"]);
        var keys = await Send(["KEYS", "a*"]);
        Assert.Equal(2, keys.Length);
    }

    [Fact]
    public async Task DbSizeFlush()
    {
        await Send(["SET", "a", "1"]);
        Assert.Equal(["1"], await Send(["DBSIZE"]));
        Assert.Equal(["OK"], await Send(["FLUSHALL"]));
        Assert.Equal(["0"], await Send(["DBSIZE"]));
    }

    [Fact] public async Task Expire() { await Send(["SET", "k", "v"]); Assert.Equal(["1"], await Send(["EXPIRE", "k", "100"])); }

    [Fact]
    public async Task Ttl()
    {
        await Send(["SET", "k", "v"]); await Send(["EXPIRE", "k", "100"]);
        var resp = await Send(["TTL", "k"]);
        Assert.True(int.Parse(resp[0]) > 0);
    }

    [Fact] public async Task TtlMissing() => Assert.Equal(["-2"], await Send(["TTL", "x"]));
    [Fact] public async Task TypeString() { await Send(["SET", "k", "v"]); Assert.Equal(["string"], await Send(["TYPE", "k"])); }
    [Fact] public async Task TypeNone() => Assert.Equal(["none"], await Send(["TYPE", "x"]));
    [Fact] public async Task UnknownCmd() => Assert.StartsWith("ERR", (await Send(["BOGUS"]))[0]);
    [Fact] public async Task WrongArity() => Assert.StartsWith("ERR", (await Send(["SET", "x"]))[0]);

    // ---- inline commands ----

    [Fact]
    public async Task InlinePing()
    {
        using var c = new TcpClient(); await c.ConnectAsync(IPAddress.Loopback, _port);
        c.GetStream().Write("PING\r\n"u8); await c.GetStream().FlushAsync();
        // Response is a simple string (+PONG), not an inline command. Use ReadValue.
        Assert.Equal(["PONG"], await new RespReader().ReadValue(c.GetStream()));
    }

    // ---- concurrency ----

    [Fact]
    public async Task Concurrent_IncrSameKey()
    {
        var tasks = new Task[20];
        for (int i = 0; i < 20; i++) tasks[i] = Task.Run(() => Send(["INCR", "shared"]));
        await Task.WhenAll(tasks);
        Assert.Equal(["20"], await Send(["GET", "shared"]));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _serverTask; } catch (OperationCanceledException) { }
    }
}
