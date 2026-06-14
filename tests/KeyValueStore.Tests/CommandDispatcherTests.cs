using System.Text;
using KeyValueStore.Server;

namespace KeyValueStore.Tests;

public class CommandDispatcherTests
{
    private readonly InMemoryStore _store = new();
    private readonly CommandDispatcher _dispatcher;

    public CommandDispatcherTests()
    {
        _dispatcher = new CommandDispatcher(_store);
    }

    private async Task<string> Execute(string[] args)
    {
        using var ms = new MemoryStream();
        var writer = new RespWriter(ms);
        await _dispatcher.ExecuteAsync(args, writer);
        ms.Position = 0;
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task<string> Exec(string commandLine)
    {
        return await Execute(commandLine.Split(' '));
    }

    // ---- PING ----

    [Fact] public async Task Ping_NoArgs_ReturnsPong() => Assert.Equal("+PONG\r\n", await Exec("PING"));
    [Fact] public async Task Ping_WithMessage_ReturnsBulk() => Assert.Equal("$5\r\nhello\r\n", await Exec("PING hello"));

    // ---- ECHO ----

    [Fact] public async Task Echo_ReturnsBulk() => Assert.Equal("$5\r\nhello\r\n", await Exec("ECHO hello"));
    [Fact] public async Task Echo_WrongArity_ReturnsError() => Assert.StartsWith("-ERR", await Exec("ECHO"));

    // ---- SET / GET ----

    [Fact] public async Task Set_Ok() => Assert.Equal("+OK\r\n", await Exec("SET foo bar"));
    [Fact] public async Task Set_ThenGet_ReturnsValue()
    {
        await Exec("SET foo bar");
        Assert.Equal("$3\r\nbar\r\n", await Exec("GET foo"));
    }
    [Fact] public async Task Get_Missing_ReturnsNull() => Assert.Equal("$-1\r\n", await Exec("GET missing"));
    [Fact] public async Task Set_WrongArity() => Assert.StartsWith("-ERR", await Exec("SET foo"));
    [Fact] public async Task Set_WithEX_SetsTTL()
    {
        await Exec("SET k v EX 3600");
        Assert.True(_store.Ttl("k") > 0);
    }

    // ---- DEL ----

    [Fact] public async Task Del_Existing_ReturnsOne()
    {
        await Exec("SET a 1");
        Assert.Equal(":1\r\n", await Exec("DEL a"));
    }
    [Fact] public async Task Del_Missing_ReturnsZero() => Assert.Equal(":0\r\n", await Exec("DEL x"));
    [Fact] public async Task Del_WrongArity() => Assert.StartsWith("-ERR", await Exec("DEL"));

    // ---- EXISTS ----

    [Fact]
    public async Task Exists_ReturnsCount()
    {
        await Exec("SET a 1"); await Exec("SET b 2");
        Assert.Equal(":2\r\n", await Exec("EXISTS a b"));
        Assert.Equal(":0\r\n", await Exec("EXISTS x"));
    }

    // ---- KEYS ----

    [Fact]
    public async Task Keys_ReturnsArray()
    {
        await Exec("SET a 1"); await Exec("SET ab 2");
        Assert.Equal("*2\r\n$1\r\na\r\n$2\r\nab\r\n", await Exec("KEYS *"));
    }
    [Fact] public async Task Keys_NoMatch_ReturnsEmptyArray() => Assert.Equal("*0\r\n", await Exec("KEYS nomatch"));

    // ---- DBSIZE / FLUSHALL ----

    [Fact]
    public async Task DbSize_ReturnsCount()
    {
        await Exec("SET a 1"); await Exec("SET b 2");
        Assert.Equal(":2\r\n", await Exec("DBSIZE"));
    }
    [Fact]
    public async Task FlushAll_ClearsStore()
    {
        await Exec("SET a 1");
        Assert.Equal("+OK\r\n", await Exec("FLUSHALL"));
        Assert.Equal(":0\r\n", await Exec("DBSIZE"));
    }

    // ---- EXPIRE / TTL ----

    [Fact]
    public async Task Expire_Existing_ReturnsOne()
    {
        await Exec("SET k v");
        Assert.Equal(":1\r\n", await Exec("EXPIRE k 100"));
    }
    [Fact] public async Task Expire_Missing_ReturnsZero() => Assert.Equal(":0\r\n", await Exec("EXPIRE x 10"));
    [Fact]
    public async Task Ttl_NoExpiry_ReturnsMinusOne()
    {
        await Exec("SET k v");
        Assert.Equal(":-1\r\n", await Exec("TTL k"));
    }
    [Fact] public async Task Ttl_Missing_ReturnsMinusTwo() => Assert.Equal(":-2\r\n", await Exec("TTL x"));

    // ---- TYPE ----

    [Fact]
    public async Task Type_String()
    {
        await Exec("SET k v");
        Assert.Equal("+string\r\n", await Exec("TYPE k"));
    }
    [Fact] public async Task Type_Set() { await Exec("SADD s a"); Assert.Equal("+set\r\n", await Exec("TYPE s")); }
    [Fact] public async Task Type_Hash() { await Exec("HSET h f v"); Assert.Equal("+hash\r\n", await Exec("TYPE h")); }
    [Fact] public async Task Type_None() => Assert.Equal("+none\r\n", await Exec("TYPE x"));

    // ---- INCR / DECR ----

    [Fact] public async Task Incr_New_ReturnsOne() => Assert.Equal(":1\r\n", await Exec("INCR c"));
    [Fact]
    public async Task Incr_Existing_Increments()
    {
        await Exec("SET c 5");
        Assert.Equal(":6\r\n", await Exec("INCR c"));
    }
    [Fact] public async Task Decr_New_ReturnsMinusOne() => Assert.Equal(":-1\r\n", await Exec("DECR c"));
    [Fact]
    public async Task Incr_NonInteger_ReturnsError()
    {
        await Exec("SET k hello");
        Assert.StartsWith("-ERR", await Exec("INCR k"));
    }

    // ---- sets ----

    [Fact] public async Task SAdd_ReturnsCount() => Assert.Equal(":2\r\n", await Exec("SADD s a b"));
    [Fact] public async Task SRem_ReturnsCount() { await Exec("SADD s a b"); Assert.Equal(":1\r\n", await Exec("SREM s a")); }
    [Fact] public async Task SMembers_ReturnsArray() { await Exec("SADD s a"); Assert.Equal("*1\r\n$1\r\na\r\n", await Exec("SMEMBERS s")); }
    [Fact] public async Task SIsMember_True() { await Exec("SADD s a"); Assert.Equal(":1\r\n", await Exec("SISMEMBER s a")); }
    [Fact] public async Task SIsMember_False() => Assert.Equal(":0\r\n", await Exec("SISMEMBER s x"));
    [Fact] public async Task SCard_ReturnsCount() { await Exec("SADD s a b"); Assert.Equal(":2\r\n", await Exec("SCARD s")); }

    // ---- hashes ----

    [Fact] public async Task HSet_ReturnsOne() => Assert.Equal(":1\r\n", await Exec("HSET h f v"));
    [Fact] public async Task HGet_ReturnsValue() { await Exec("HSET h f v"); Assert.Equal("$1\r\nv\r\n", await Exec("HGET h f")); }
    [Fact] public async Task HGet_Missing_ReturnsNull() => Assert.Equal("$-1\r\n", await Exec("HGET h f"));
    [Fact] public async Task HDel_ReturnsCount() { await Exec("HSET h a 1"); await Exec("HSET h b 2"); Assert.Equal(":1\r\n", await Exec("HDEL h a")); }
    [Fact] public async Task HGetAll_ReturnsArray() { await Exec("HSET h a 1"); Assert.Equal("*2\r\n$1\r\na\r\n$1\r\n1\r\n", await Exec("HGETALL h")); }
    [Fact] public async Task HExists_True() { await Exec("HSET h f v"); Assert.Equal(":1\r\n", await Exec("HEXISTS h f")); }
    [Fact] public async Task HLen_ReturnsCount() { await Exec("HSET h a 1"); await Exec("HSET h b 2"); Assert.Equal(":2\r\n", await Exec("HLEN h")); }

    // ---- unknown command ----

    [Fact]
    public async Task UnknownCommand_ReturnsError()
    {
        Assert.Equal("-ERR unknown command 'FOO'\r\n", await Exec("FOO bar"));
    }

    // ---- QUIT ----

    [Fact]
    public async Task Quit_ReturnsOk_ThenThrows()
    {
        using var ms = new MemoryStream();
        var writer = new RespWriter(ms);
        var ex = await Assert.ThrowsAsync<QuitException>(async () =>
        {
            await _dispatcher.ExecuteAsync(["QUIT"], writer);
        });
        ms.Position = 0;
        Assert.Equal("+OK\r\n", Encoding.UTF8.GetString(ms.ToArray()));
    }
}
