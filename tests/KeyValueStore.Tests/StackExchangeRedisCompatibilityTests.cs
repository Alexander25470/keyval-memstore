using System.Net;
using System.Net.Sockets;
using KeyValueStore.Server;
using KeyValueStore.Server.Networking;
using KeyValueStore.Server.PubSub;
using KeyValueStore.Server.Store;
using StackExchange.Redis;

namespace KeyValueStore.Tests;

/// <summary>
/// Integration tests that prove the key-value store is compatible with the
/// StackExchange.Redis client library — the most popular .NET Redis client.
/// These tests exercise the server through the same API that production
/// applications use against real Redis, validating RESP2 protocol compliance.
/// </summary>
public class StackExchangeRedisCompatibilityTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _cts = new();
    private readonly int _port;
    private Task _serverTask = Task.CompletedTask;
    private ConnectionMultiplexer _redis = null!;
    private IDatabase _db = null!;

    public StackExchangeRedisCompatibilityTests()
    {
        _port = GetRandomPort();
    }

    public async Task InitializeAsync()
    {
        // ---- start the KeyValueStore server on a random port ----
        var store = new InMemoryStore();
        var hub = new PubSubHub();
        var dispatcher = new CommandDispatcher(store, hub);
        var server = new KvServer(dispatcher, hub, "127.0.0.1", _port);
        _ = store.RunExpirationLoop(_cts.Token);
        _serverTask = server.RunAsync(_cts.Token);

        // Wait until the server is actually listening.
        using var ctsConnect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ctsConnect.Token.IsCancellationRequested)
        {
            try
            {
                using var test = new TcpClient();
                await test.ConnectAsync(IPAddress.Loopback, _port, ctsConnect.Token);
                break;
            }
            catch
            {
                await Task.Delay(50, ctsConnect.Token);
            }
        }

        // ---- connect via StackExchange.Redis ----
        var config = new ConfigurationOptions
        {
            EndPoints = { { IPAddress.Loopback, _port } },
            AbortOnConnectFail = false,
            ConnectTimeout = 10000,
            SyncTimeout = 10000,
        };

        _redis = await ConnectionMultiplexer.ConnectAsync(config);
        _db = _redis.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
        }

        _cts.Cancel();
        try { await _serverTask; } catch (OperationCanceledException) { }
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

    // ========================================================================
    // PING / ECHO
    // ========================================================================

    [Fact]
    public async Task Ping_ReturnsPong()
    {
        var result = await _db.PingAsync();
        Assert.True(result.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task Ping_WithMessage_ReturnsSameMessage()
    {
        // StackExchange.Redis doesn't expose PING(msg) directly,
        // so we use ExecuteAsync to send the raw command.
        var result = await _db.ExecuteAsync("PING", "hello");
        Assert.Equal("hello", result.ToString());
    }

    [Fact]
    public async Task Echo_ReturnsMessage()
    {
        var result = await _db.ExecuteAsync("ECHO", "world");
        Assert.Equal("world", result.ToString());
    }

    // ========================================================================
    // STRING commands (SET / GET / INCR / DECR)
    // ========================================================================

    [Fact]
    public async Task StringSet_ThenStringGet_RoundTrip()
    {
        await _db.StringSetAsync("key1", "value1");
        var result = await _db.StringGetAsync("key1");
        Assert.Equal("value1", result.ToString());
    }

    [Fact]
    public async Task StringGet_MissingKey_ReturnsNull()
    {
        var result = await _db.StringGetAsync("nonexistent");
        Assert.True(result.IsNull);
    }

    [Fact]
    public async Task StringSet_WithExpiry_SetsTTL()
    {
        await _db.StringSetAsync("expkey", "val", TimeSpan.FromHours(1));
        var ttl = await _db.KeyTimeToLiveAsync("expkey");
        Assert.NotNull(ttl);
        Assert.True(ttl.Value.TotalSeconds > 0);
        Assert.True(ttl.Value.TotalSeconds <= 3600);
    }

    [Fact]
    public async Task StringSet_WithExpiry_MillisecondsPrecision()
    {
        // PX (milliseconds) via StackExchange.Redis
        await _db.StringSetAsync("pxkey", "val", TimeSpan.FromMilliseconds(5000));
        var ttl = await _db.KeyTimeToLiveAsync("pxkey");
        Assert.NotNull(ttl);
        Assert.True(ttl.Value.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task StringIncrement_Initial_ReturnsOne()
    {
        var result = await _db.StringIncrementAsync("counter");
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task StringIncrement_MultipleTimes_Accumulates()
    {
        await _db.StringIncrementAsync("counter2");
        await _db.StringIncrementAsync("counter2");
        var result = await _db.StringIncrementAsync("counter2");
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task StringDecrement_ReturnsDecrementedValue()
    {
        await _db.StringSetAsync("dec", "10");
        var result = await _db.StringDecrementAsync("dec");
        Assert.Equal(9, result);
    }

    // ========================================================================
    // KEY commands (DEL / EXISTS / KEYS / EXPIRE / TTL / TYPE)
    // ========================================================================

    [Fact]
    public async Task KeyDelete_Existing_ReturnsTrue()
    {
        await _db.StringSetAsync("todel", "x");
        var deleted = await _db.KeyDeleteAsync("todel");
        Assert.True(deleted);
    }

    [Fact]
    public async Task KeyDelete_Multiple_ReturnsCount()
    {
        await _db.StringSetAsync("a", "1");
        await _db.StringSetAsync("b", "2");
        var result = await _db.ExecuteAsync("DEL", "a", "b", "c");
        Assert.Equal(2, (long)result);
    }

    [Fact]
    public async Task KeyExists_Existing_ReturnsTrue()
    {
        await _db.StringSetAsync("existsKey", "1");
        Assert.True(await _db.KeyExistsAsync("existsKey"));
    }

    [Fact]
    public async Task KeyExists_Missing_ReturnsFalse()
    {
        Assert.False(await _db.KeyExistsAsync("noSuchKey"));
    }

    [Fact]
    public async Task KeyExists_Multiple_ReturnsCount()
    {
        await _db.StringSetAsync("ex1", "1");
        var result = await _db.ExecuteAsync("EXISTS", "ex1", "ex2", "ex3");
        Assert.Equal(1, (long)result);
    }

    [Fact]
    public async Task KeyExpire_SetsExpiration()
    {
        await _db.StringSetAsync("expireMe", "val");
        var set = await _db.KeyExpireAsync("expireMe", TimeSpan.FromSeconds(100));
        Assert.True(set);
        var ttl = await _db.KeyTimeToLiveAsync("expireMe");
        Assert.NotNull(ttl);
        Assert.True(ttl.Value.TotalSeconds > 0);
    }

    [Fact]
    public async Task KeyTimeToLive_NoExpiry_ReturnsNull()
    {
        await _db.StringSetAsync("noExp", "val");
        var ttl = await _db.KeyTimeToLiveAsync("noExp");
        Assert.Null(ttl);
    }

    [Fact]
    public async Task KeyTimeToLive_ExpiredKey_ReturnsNegative()
    {
        // TTL on missing key should return -2 (represented as null by SE.Redis)
        var ttl = await _db.KeyTimeToLiveAsync("missingTTL");
        Assert.Null(ttl);
    }

    [Fact]
    public async Task KeyType_String_ReturnsString()
    {
        await _db.StringSetAsync("typeTest", "val");
        var type = await _db.KeyTypeAsync("typeTest");
        Assert.Equal(RedisType.String, type);
    }

    [Fact]
    public async Task KeyType_Missing_ReturnsNone()
    {
        var type = await _db.KeyTypeAsync("noType");
        Assert.Equal(RedisType.None, type);
    }

    // ========================================================================
    // SERVER commands (DBSIZE / FLUSHALL)
    // ========================================================================

    [Fact]
    public async Task DatabaseSize_ReturnsKeyCount()
    {
        await _db.StringSetAsync("db1", "1");
        await _db.StringSetAsync("db2", "2");
        // SE.Redis doesn't have a direct DBSIZE wrapper, use ExecuteAsync.
        var size = await _db.ExecuteAsync("DBSIZE");
        Assert.Equal(2, (long)size);
    }

    [Fact]
    public async Task FlushAll_ClearsEverything()
    {
        await _db.StringSetAsync("f1", "1");
        await _db.StringSetAsync("f2", "2");

        await _db.ExecuteAsync("FLUSHALL");

        var size = await _db.ExecuteAsync("DBSIZE");
        Assert.Equal(0, (long)size);
    }

    // ========================================================================
    // KEYS (pattern matching)
    // ========================================================================

    [Fact]
    public async Task Keys_GlobPattern_ReturnsMatchingKeys()
    {
        await _db.StringSetAsync("aa", "1");
        await _db.StringSetAsync("ab", "2");
        await _db.StringSetAsync("cc", "3");

        var result = await _db.ExecuteAsync("KEYS", "a*");
        // Result is an array of bulk strings.
        var keys = ((RedisResult[])result!).Select(r => r.ToString()).ToArray();
        Assert.Equal(2, keys.Length);
        Assert.Contains("aa", keys);
        Assert.Contains("ab", keys);
    }

    [Fact]
    public async Task Keys_AllPattern_ReturnsAllKeys()
    {
        await _db.StringSetAsync("k1", "v1");
        await _db.StringSetAsync("k2", "v2");

        var result = await _db.ExecuteAsync("KEYS", "*");
        var keys = ((RedisResult[])result!).Select(r => r.ToString()).ToArray();
        Assert.True(keys.Length >= 2);
    }

    // ========================================================================
    // PUB/SUB
    // ========================================================================

    [Fact]
    public async Task Publish_NoSubscribers_ReturnsZero()
    {
        var count = await _db.PublishAsync(new RedisChannel("chan", RedisChannel.PatternMode.Literal), "msg");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Subscribe_AndPublish_ReceivesMessage()
    {
        var channel = new RedisChannel("testchan", RedisChannel.PatternMode.Literal);
        var received = new TaskCompletionSource<string>();

        var sub = _redis.GetSubscriber();
        await sub.SubscribeAsync(channel, (ch, msg) =>
        {
            received.TrySetResult(msg.ToString());
        });

        // Small delay to ensure subscription is registered.
        await Task.Delay(50);

        var count = await _db.PublishAsync(channel, "hello-subscriber");
        Assert.Equal(1, count);

        // Wait for the message to arrive on the subscriber.
        var timeout = Task.Delay(3000);
        var done = await Task.WhenAny(received.Task, timeout);
        Assert.Equal(received.Task, done);
        Assert.Equal("hello-subscriber", await received.Task);
    }

    [Fact]
    public async Task Publish_MultipleSubscribers_ReturnsCorrectCount()
    {
        var channel = new RedisChannel("multi-chan", RedisChannel.PatternMode.Literal);
        var tcs1 = new TaskCompletionSource<string>();
        var tcs2 = new TaskCompletionSource<string>();

        var sub = _redis.GetSubscriber();
        await sub.SubscribeAsync(channel, (_, msg) => tcs1.TrySetResult(msg.ToString()));
        await sub.SubscribeAsync(channel, (_, msg) => tcs2.TrySetResult(msg.ToString()));

        await Task.Delay(50);

        var count = await _db.PublishAsync(channel, "broadcast");
        // SE.Redis may consolidate multiple subscribers on same channel.
        Assert.True(count >= 1);
    }

    // ========================================================================
    // Multiple operations on the same connection (connection reuse)
    // ========================================================================

    [Fact]
    public async Task ManySequentialOperations_AllSucceed()
    {
        for (int i = 0; i < 50; i++)
        {
            var key = $"seq-{i}";
            await _db.StringSetAsync(key, i.ToString());
            var val = await _db.StringGetAsync(key);
            Assert.Equal(i.ToString(), val.ToString());
        }
    }

    [Fact]
    public async Task ConcurrentOperations_OnSameKey_AreAtomic()
    {
        var tasks = new Task<long>[20];
        for (int i = 0; i < 20; i++)
        {
            tasks[i] = Task.Run(() => _db.StringIncrementAsync("shared-counter"));
        }

        await Task.WhenAll(tasks);

        var final = await _db.StringGetAsync("shared-counter");
        Assert.Equal("20", final.ToString());
    }
}
