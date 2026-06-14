using System.Text;
using KeyValueStore.Server;
using KeyValueStore.Server.Store;

namespace KeyValueStore.Tests;

public class InMemoryStoreTests
{
    private readonly InMemoryStore _store = new();

    private static ReadOnlyMemory<byte> B(string s) => Encoding.ASCII.GetBytes(s);
    private static string? S(byte[]? b) => b is null ? null : Encoding.ASCII.GetString(b);

    // ---- basic get / set ----

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        Assert.Null(_store.Get(B("nonexistent")));
    }

    [Fact]
    public void Set_ThenGet_ReturnsValue()
    {
        _store.Set(B("foo"), B("bar"));
        Assert.Equal("bar", S(_store.Get(B("foo"))));
    }

    [Fact]
    public void Set_Overwrite_ReturnsLatest()
    {
        _store.Set(B("foo"), B("v1"));
        _store.Set(B("foo"), B("v2"));
        Assert.Equal("v2", S(_store.Get(B("foo"))));
    }

    // ---- delete ----

    [Fact]
    public void Delete_Existing_ReturnsOne()
    {
        _store.Set(B("a"), B("1"));
        Assert.Equal(1, _store.Delete(B("a")));
        Assert.Null(_store.Get(B("a")));
    }

    [Fact]
    public void Delete_Missing_ReturnsZero()
    {
        Assert.Equal(0, _store.Delete(B("x")));
    }

    [Fact]
    public void Delete_Multiple_CountsOnlyExisting()
    {
        _store.Set(B("a"), B("1"));
        _store.Set(B("b"), B("2"));
        Assert.Equal(2, _store.Delete(B("a"), B("b"), B("c")));
    }

    // ---- exists ----

    [Fact]
    public void Exists_ReturnsCorrectCount()
    {
        _store.Set(B("a"), B("1"));
        _store.Set(B("b"), B("2"));
        Assert.Equal(2, _store.Exists(B("a"), B("b")));
        Assert.Equal(0, _store.Exists(B("x")));
        Assert.Equal(1, _store.Exists(B("a"), B("x")));
    }

    // ---- keys ----

    [Fact]
    public void Keys_Star_ReturnsAll()
    {
        _store.Set(B("a"), B("1"));
        _store.Set(B("ab"), B("2"));
        _store.Set(B("abc"), B("3"));
        var k = _store.Keys(B("*"));
        Assert.Equal(3, k.Length);
    }

    [Fact]
    public void Keys_Prefix_ReturnsFiltered()
    {
        _store.Set(B("prefix_one"), B("1"));
        _store.Set(B("prefix_two"), B("2"));
        _store.Set(B("other"), B("3"));
        var k = _store.Keys(B("prefix*"));
        Assert.Equal(2, k.Length);
        Assert.Contains(k, b => S(b) == "prefix_one");
        Assert.Contains(k, b => S(b) == "prefix_two");
    }

    [Fact]
    public void Keys_Suffix_ReturnsFiltered()
    {
        _store.Set(B("one_suffix"), B("1"));
        _store.Set(B("two_other"), B("2"));
        var k = _store.Keys(B("*suffix"));
        Assert.Equal(1, k.Length);
        Assert.Equal("one_suffix", S(k[0]));
    }

    [Fact]
    public void Keys_Question_MatchesSingleChar()
    {
        _store.Set(B("hallo"), B("1"));
        _store.Set(B("hello"), B("2"));
        _store.Set(B("hllo"), B("3"));
        var k = _store.Keys(B("h?llo"));
        Assert.Equal(2, k.Length);
    }

    [Fact]
    public void Keys_NoMatch_ReturnsEmpty()
    {
        _store.Set(B("a"), B("1"));
        Assert.Empty(_store.Keys(B("nomatch*")));
    }

    // ---- dbsize / flush ----

    [Fact]
    public void DBSize_ReflectsKeys()
    {
        _store.Set(B("a"), B("1"));
        _store.Set(B("b"), B("2"));
        Assert.Equal(2, _store.DBSize());
    }

    [Fact]
    public void FlushAll_ClearsEverything()
    {
        _store.Set(B("a"), B("1"));
        _store.Set(B("b"), B("2"));
        _store.FlushAll();
        Assert.Equal(0, _store.DBSize());
        Assert.Null(_store.Get(B("a")));
    }

    // ---- ttl ----

    [Fact]
    public void Set_WithTTL_GetBeforeExpiry_ReturnsValue()
    {
        _store.Set(B("temp"), B("val"), TimeSpan.FromHours(1));
        Assert.Equal("val", S(_store.Get(B("temp"))));
    }

    [Fact]
    public void Set_WithTTL_GetAfterExpiry_ReturnsNull()
    {
        _store.Set(B("temp"), B("val"), TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);
        Assert.Null(_store.Get(B("temp")));
    }

    [Fact]
    public void Expire_SetsTTL_OnExistingKey()
    {
        _store.Set(B("k"), B("v"));
        Assert.True(_store.Expire(B("k"), 3600));
        var ttl = _store.Ttl(B("k"));
        Assert.True(ttl > 0 && ttl <= 3600);
    }

    [Fact]
    public void Expire_OnMissingKey_ReturnsFalse()
    {
        Assert.False(_store.Expire(B("missing"), 10));
    }

    [Fact]
    public void Ttl_NoExpiry_ReturnsNegativeOne()
    {
        _store.Set(B("k"), B("v"));
        Assert.Equal(-1, _store.Ttl(B("k")));
    }

    [Fact]
    public void Ttl_MissingKey_ReturnsNegativeTwo()
    {
        Assert.Equal(-2, _store.Ttl(B("missing")));
    }

    [Fact]
    public void Ttl_ExpiredKey_ReturnsNegativeTwo()
    {
        _store.Set(B("temp"), B("val"), TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);
        Assert.Equal(-2, _store.Ttl(B("temp")));
    }

    // ---- incr / decr ----

    [Fact]
    public void Incr_NewKey_ReturnsOne()
    {
        Assert.Equal(1, _store.Incr(B("counter")));
        Assert.Equal("1", S(_store.Get(B("counter"))));
    }

    [Fact]
    public void Incr_ExistingKey_Increments()
    {
        _store.Set(B("counter"), B("5"));
        Assert.Equal(6, _store.Incr(B("counter")));
    }

    [Fact]
    public void Decr_NewKey_ReturnsMinusOne()
    {
        Assert.Equal(-1, _store.Decr(B("counter")));
    }

    [Fact]
    public void Decr_ExistingKey_Decrements()
    {
        _store.Set(B("counter"), B("10"));
        Assert.Equal(9, _store.Decr(B("counter")));
    }

    [Fact]
    public void Incr_NonInteger_Throws()
    {
        _store.Set(B("k"), B("hello"));
        Assert.Throws<InvalidOperationException>(() => _store.Incr(B("k")));
    }

    [Fact]
    public void Incr_ExpiredKey_StartsFromZero()
    {
        _store.Set(B("temp"), B("42"), TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);
        Assert.Equal(1, _store.Incr(B("temp")));
    }

    // ---- keys (expired) ----

    [Fact]
    public void Keys_ExpiredKey_Excluded()
    {
        _store.Set(B("keep"), B("val"));
        _store.Set(B("exp"), B("val"), TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);
        var keys = _store.Keys(B("*"));
        Assert.DoesNotContain(keys, k => S(k) == "exp");
        Assert.Contains(keys, k => S(k) == "keep");
    }

    // ---- concurrent writes ----

    [Fact]
    public void ConcurrentWrites_NoExceptions_CountCorrect()
    {
        const int count = 10000;
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        Parallel.For(0, count, opts, i =>
        {
            _store.Set(B($"k{i}"), B(i.ToString()));
        });

        Assert.Equal(count, _store.DBSize());
    }

    [Fact]
    public async Task Concurrent_Incr_SameKey_ReturnsCorrectTotal()
    {
        const int tasks = 100;
        var all = new Task[tasks];
        for (int i = 0; i < tasks; i++)
        {
            all[i] = Task.Run(() => _store.Incr(B("shared")));
        }
        await Task.WhenAll(all);

        // 100 concurrent incrs on a fresh key should yield exactly 100.
        Assert.Equal(100, _store.Incr(B("shared")) - 1);
    }

    // ---- active expiration ----

    [Fact]
    public async Task ActiveExpiration_RemovesExpiredKeys()
    {
        // Set many keys that expire almost immediately.
        for (int i = 0; i < 50; i++)
            _store.Set(B($"exp{i}"), B("v"), TimeSpan.FromMilliseconds(20));

        // Start the expiration loop.
        using var cts = new CancellationTokenSource();
        var loop = _store.RunExpirationLoop(cts.Token);

        // Wait long enough for minimal TTL to pass plus several sampling cycles.
        await Task.Delay(500);
        cts.Cancel();

        try { await loop; } catch (OperationCanceledException) { }

        Assert.Equal(0, _store.DBSize());
    }

    // ---- sets ----

    [Fact]
    public void SAdd_NewSet_ReturnsCount()
    {
        Assert.Equal(2, _store.SAdd(B("s"), B("a"), B("b")));
        Assert.Equal(1, _store.SAdd(B("s"), B("c")));
    }

    [Fact]
    public void SAdd_Duplicates_NotCounted()
    {
        _store.SAdd(B("s"), B("a"), B("b"));
        Assert.Equal(1, _store.SAdd(B("s"), B("a"), B("c"))); // only c is new
    }

    [Fact]
    public void SRem_RemovesMembers()
    {
        _store.SAdd(B("s"), B("a"), B("b"), B("c"));
        Assert.Equal(2, _store.SRem(B("s"), B("a"), B("c"), B("x")));
    }

    [Fact]
    public void SRem_MissingKey_ReturnsZero()
    {
        Assert.Equal(0, _store.SRem(B("missing"), B("a")));
    }

    [Fact]
    public void SMembers_ReturnsAll()
    {
        _store.SAdd(B("s"), B("a"), B("b"));
        var m = _store.SMembers(B("s"));
        Assert.Equal(2, m.Length);
    }

    [Fact]
    public void SMembers_Missing_ReturnsEmpty()
    {
        Assert.Empty(_store.SMembers(B("x")));
    }

    [Fact]
    public void SIsMember_ChecksExistence()
    {
        _store.SAdd(B("s"), B("a"));
        Assert.True(_store.SIsMember(B("s"), B("a")));
        Assert.False(_store.SIsMember(B("s"), B("b")));
    }

    [Fact]
    public void SCard_ReturnsCount() { _store.SAdd(B("s"), B("a"), B("b")); Assert.Equal(2, _store.SCard(B("s"))); }
    [Fact]
    public void SCard_Missing_ReturnsZero() => Assert.Equal(0, _store.SCard(B("x")));

    [Fact]
    public void Set_GetOnSet_ReturnsNull()
    {
        _store.SAdd(B("s"), B("a"));
        Assert.Null(_store.Get(B("s")));
    }

    // ---- hashes ----

    [Fact]
    public void HSet_NewField_ReturnsOne()
    {
        Assert.Equal(1, _store.HSet(B("h"), B("name"), B("Alice")));
        Assert.Equal(1, _store.HSet(B("h"), B("age"), B("30")));
    }

    [Fact]
    public void HSet_Overwrite_SameCount() => Assert.Equal(1, _store.HSet(B("h"), B("f"), B("v2")));

    [Fact]
    public void HGet_ReturnsValue()
    {
        _store.HSet(B("h"), B("f"), B("v"));
        Assert.Equal("v", Encoding.ASCII.GetString(_store.HGet(B("h"), B("f"))!));
    }

    [Fact]
    public void HGet_Missing_ReturnsNull()
    {
        Assert.Null(_store.HGet(B("x"), B("f")));
        _store.HSet(B("h"), B("f"), B("v"));
        Assert.Null(_store.HGet(B("h"), B("g")));
    }

    [Fact]
    public void HDel_RemovesFields()
    {
        _store.HSet(B("h"), B("a"), B("1")); _store.HSet(B("h"), B("b"), B("2"));
        Assert.Equal(1, _store.HDel(B("h"), B("a"), B("x")));
    }

    [Fact]
    public void HDel_Missing_ReturnsZero() => Assert.Equal(0, _store.HDel(B("x"), B("f")));

    [Fact]
    public void HGetAll_ReturnsAll()
    {
        _store.HSet(B("h"), B("a"), B("1")); _store.HSet(B("h"), B("b"), B("2"));
        var all = _store.HGetAll(B("h"));
        Assert.Equal(4, all.Length); // key,value,key,value
    }

    [Fact]
    public void HExists_ChecksField()
    {
        _store.HSet(B("h"), B("f"), B("v"));
        Assert.True(_store.HExists(B("h"), B("f")));
        Assert.False(_store.HExists(B("h"), B("g")));
    }

    [Fact]
    public void HLen_ReturnsCount() { _store.HSet(B("h"), B("a"), B("1")); _store.HSet(B("h"), B("b"), B("2")); Assert.Equal(2, _store.HLen(B("h"))); }
    [Fact]
    public void HLen_Missing_ReturnsZero() => Assert.Equal(0, _store.HLen(B("x")));

    [Fact]
    public void Hash_GetOnHash_ReturnsNull()
    {
        _store.HSet(B("h"), B("f"), B("v"));
        Assert.Null(_store.Get(B("h")));
    }

    // ---- type ----

    [Fact]
    public void Type_ReturnsCorrect()
    {
        _store.Set(B("s"), B("v"));
        _store.SAdd(B("set"), B("a"));
        _store.HSet(B("hash"), B("f"), B("v"));
        Assert.Equal("string", _store.Type(B("s")));
        Assert.Equal("set", _store.Type(B("set")));
        Assert.Equal("hash", _store.Type(B("hash")));
        Assert.Equal("none", _store.Type(B("missing")));
    }
}



