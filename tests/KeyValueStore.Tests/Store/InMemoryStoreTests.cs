using KeyValueStore.Server;

namespace KeyValueStore.Tests;

public class InMemoryStoreTests
{
    private readonly InMemoryStore _store = new();

    // ---- basic get / set ----

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        Assert.Null(_store.Get("nonexistent"));
    }

    [Fact]
    public void Set_ThenGet_ReturnsValue()
    {
        _store.Set("foo", "bar");
        Assert.Equal("bar", _store.Get("foo"));
    }

    [Fact]
    public void Set_Overwrite_ReturnsLatest()
    {
        _store.Set("foo", "v1");
        _store.Set("foo", "v2");
        Assert.Equal("v2", _store.Get("foo"));
    }

    // ---- delete ----

    [Fact]
    public void Delete_Existing_ReturnsOne()
    {
        _store.Set("a", "1");
        Assert.Equal(1, _store.Delete("a"));
        Assert.Null(_store.Get("a"));
    }

    [Fact]
    public void Delete_Missing_ReturnsZero()
    {
        Assert.Equal(0, _store.Delete("x"));
    }

    [Fact]
    public void Delete_Multiple_CountsOnlyExisting()
    {
        _store.Set("a", "1");
        _store.Set("b", "2");
        Assert.Equal(2, _store.Delete("a", "b", "c"));
    }

    // ---- exists ----

    [Fact]
    public void Exists_ReturnsCorrectCount()
    {
        _store.Set("a", "1");
        _store.Set("b", "2");
        Assert.Equal(2, _store.Exists("a", "b"));
        Assert.Equal(0, _store.Exists("x"));
        Assert.Equal(1, _store.Exists("a", "x"));
    }

    // ---- keys ----

    [Fact]
    public void Keys_Star_ReturnsAll()
    {
        _store.Set("a", "1");
        _store.Set("ab", "2");
        _store.Set("abc", "3");
        var k = _store.Keys("*");
        Assert.Equal(3, k.Count);
    }

    [Fact]
    public void Keys_Prefix_ReturnsFiltered()
    {
        _store.Set("prefix_one", "1");
        _store.Set("prefix_two", "2");
        _store.Set("other", "3");
        var k = _store.Keys("prefix*");
        Assert.Equal(2, k.Count);
        Assert.Contains("prefix_one", k);
        Assert.Contains("prefix_two", k);
    }

    [Fact]
    public void Keys_Suffix_ReturnsFiltered()
    {
        _store.Set("one_suffix", "1");
        _store.Set("two_other", "2");
        var k = _store.Keys("*suffix");
        Assert.Single(k);
        Assert.Equal("one_suffix", k[0]);
    }

    [Fact]
    public void Keys_Question_MatchesSingleChar()
    {
        _store.Set("hallo", "1");
        _store.Set("hello", "2");
        _store.Set("hllo", "3");
        var k = _store.Keys("h?llo");
        Assert.Equal(2, k.Count);
    }

    [Fact]
    public void Keys_NoMatch_ReturnsEmpty()
    {
        _store.Set("a", "1");
        Assert.Empty(_store.Keys("nomatch*"));
    }

    // ---- dbsize / flush ----

    [Fact]
    public void DBSize_ReflectsKeys()
    {
        _store.Set("a", "1");
        _store.Set("b", "2");
        Assert.Equal(2, _store.DBSize());
    }

    [Fact]
    public void FlushAll_ClearsEverything()
    {
        _store.Set("a", "1");
        _store.Set("b", "2");
        _store.FlushAll();
        Assert.Equal(0, _store.DBSize());
        Assert.Null(_store.Get("a"));
    }

    // ---- ttl ----

    [Fact]
    public void Set_WithTTL_GetBeforeExpiry_ReturnsValue()
    {
        _store.Set("temp", "val", TimeSpan.FromHours(1));
        Assert.Equal("val", _store.Get("temp"));
    }

    [Fact]
    public void Set_WithTTL_GetAfterExpiry_ReturnsNull()
    {
        _store.Set("temp", "val", TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);
        Assert.Null(_store.Get("temp"));
    }

    [Fact]
    public void Expire_SetsTTL_OnExistingKey()
    {
        _store.Set("k", "v");
        Assert.True(_store.Expire("k", 3600));
        var ttl = _store.Ttl("k");
        Assert.True(ttl > 0 && ttl <= 3600);
    }

    [Fact]
    public void Expire_OnMissingKey_ReturnsFalse()
    {
        Assert.False(_store.Expire("missing", 10));
    }

    [Fact]
    public void Ttl_NoExpiry_ReturnsNegativeOne()
    {
        _store.Set("k", "v");
        Assert.Equal(-1, _store.Ttl("k"));
    }

    [Fact]
    public void Ttl_MissingKey_ReturnsNegativeTwo()
    {
        Assert.Equal(-2, _store.Ttl("missing"));
    }

    [Fact]
    public void Ttl_ExpiredKey_ReturnsNegativeTwo()
    {
        _store.Set("temp", "val", TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);
        Assert.Equal(-2, _store.Ttl("temp"));
    }

    // ---- incr / decr ----

    [Fact]
    public void Incr_NewKey_ReturnsOne()
    {
        Assert.Equal(1, _store.Incr("counter"));
        Assert.Equal("1", _store.Get("counter"));
    }

    [Fact]
    public void Incr_ExistingKey_Increments()
    {
        _store.Set("counter", "5");
        Assert.Equal(6, _store.Incr("counter"));
    }

    [Fact]
    public void Decr_NewKey_ReturnsMinusOne()
    {
        Assert.Equal(-1, _store.Decr("counter"));
    }

    [Fact]
    public void Decr_ExistingKey_Decrements()
    {
        _store.Set("counter", "10");
        Assert.Equal(9, _store.Decr("counter"));
    }

    [Fact]
    public void Incr_NonInteger_Throws()
    {
        _store.Set("k", "hello");
        Assert.Throws<InvalidOperationException>(() => _store.Incr("k"));
    }

    [Fact]
    public void Incr_ExpiredKey_StartsFromZero()
    {
        _store.Set("temp", "42", TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);
        Assert.Equal(1, _store.Incr("temp"));
    }

    // ---- keys (expired) ----

    [Fact]
    public void Keys_ExpiredKey_Excluded()
    {
        _store.Set("keep", "val");
        _store.Set("exp", "val", TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);
        var keys = _store.Keys("*");
        Assert.DoesNotContain("exp", keys);
        Assert.Contains("keep", keys);
    }

    // ---- concurrent writes ----

    [Fact]
    public void ConcurrentWrites_NoExceptions_CountCorrect()
    {
        const int count = 10000;
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        Parallel.For(0, count, opts, i =>
        {
            _store.Set($"k{i}", i.ToString());
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
            all[i] = Task.Run(() => _store.Incr("shared"));
        }
        await Task.WhenAll(all);

        // 100 concurrent incrs on a fresh key should yield exactly 100.
        Assert.Equal(100, _store.Incr("shared") - 1);
    }

    // ---- active expiration ----

    [Fact]
    public async Task ActiveExpiration_RemovesExpiredKeys()
    {
        // Set many keys that expire almost immediately.
        for (int i = 0; i < 50; i++)
            _store.Set($"exp{i}", "v", TimeSpan.FromMilliseconds(20));

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
        Assert.Equal(2, _store.SAdd("s", "a", "b"));
        Assert.Equal(1, _store.SAdd("s", "c"));
    }

    [Fact]
    public void SAdd_Duplicates_NotCounted()
    {
        _store.SAdd("s", "a", "b");
        Assert.Equal(1, _store.SAdd("s", "a", "c")); // only c is new
    }

    [Fact]
    public void SRem_RemovesMembers()
    {
        _store.SAdd("s", "a", "b", "c");
        Assert.Equal(2, _store.SRem("s", "a", "c", "x"));
    }

    [Fact]
    public void SRem_MissingKey_ReturnsZero()
    {
        Assert.Equal(0, _store.SRem("missing", "a"));
    }

    [Fact]
    public void SMembers_ReturnsAll()
    {
        _store.SAdd("s", "a", "b");
        var m = _store.SMembers("s");
        Assert.Equal(2, m.Length);
    }

    [Fact]
    public void SMembers_Missing_ReturnsEmpty()
    {
        Assert.Empty(_store.SMembers("x"));
    }

    [Fact]
    public void SIsMember_ChecksExistence()
    {
        _store.SAdd("s", "a");
        Assert.True(_store.SIsMember("s", "a"));
        Assert.False(_store.SIsMember("s", "b"));
    }

    [Fact]
    public void SCard_ReturnsCount() { _store.SAdd("s", "a", "b"); Assert.Equal(2, _store.SCard("s")); }
    [Fact]
    public void SCard_Missing_ReturnsZero() => Assert.Equal(0, _store.SCard("x"));

    [Fact]
    public void Set_GetOnSet_ReturnsNull()
    {
        _store.SAdd("s", "a");
        Assert.Null(_store.Get("s"));
    }

    // ---- hashes ----

    [Fact]
    public void HSet_NewField_ReturnsOne()
    {
        Assert.Equal(1, _store.HSet("h", "name", "Alice"));
        Assert.Equal(1, _store.HSet("h", "age", "30"));
    }

    [Fact]
    public void HSet_Overwrite_SameCount() => Assert.Equal(1, _store.HSet("h", "f", "v2"));

    [Fact]
    public void HGet_ReturnsValue()
    {
        _store.HSet("h", "f", "v");
        Assert.Equal("v", _store.HGet("h", "f"));
    }

    [Fact]
    public void HGet_Missing_ReturnsNull()
    {
        Assert.Null(_store.HGet("x", "f"));
        _store.HSet("h", "f", "v");
        Assert.Null(_store.HGet("h", "g"));
    }

    [Fact]
    public void HDel_RemovesFields()
    {
        _store.HSet("h", "a", "1"); _store.HSet("h", "b", "2");
        Assert.Equal(1, _store.HDel("h", "a", "x"));
    }

    [Fact]
    public void HDel_Missing_ReturnsZero() => Assert.Equal(0, _store.HDel("x", "f"));

    [Fact]
    public void HGetAll_ReturnsAll()
    {
        _store.HSet("h", "a", "1"); _store.HSet("h", "b", "2");
        var all = _store.HGetAll("h");
        Assert.Equal(4, all.Length); // key,value,key,value
    }

    [Fact]
    public void HExists_ChecksField()
    {
        _store.HSet("h", "f", "v");
        Assert.True(_store.HExists("h", "f"));
        Assert.False(_store.HExists("h", "g"));
    }

    [Fact]
    public void HLen_ReturnsCount() { _store.HSet("h", "a", "1"); _store.HSet("h", "b", "2"); Assert.Equal(2, _store.HLen("h")); }
    [Fact]
    public void HLen_Missing_ReturnsZero() => Assert.Equal(0, _store.HLen("x"));

    [Fact]
    public void Hash_GetOnHash_ReturnsNull()
    {
        _store.HSet("h", "f", "v");
        Assert.Null(_store.Get("h"));
    }

    // ---- type ----

    [Fact]
    public void Type_ReturnsCorrect()
    {
        _store.Set("s", "v");
        _store.SAdd("set", "a");
        _store.HSet("hash", "f", "v");
        Assert.Equal("string", _store.Type("s"));
        Assert.Equal("set", _store.Type("set"));
        Assert.Equal("hash", _store.Type("hash"));
        Assert.Equal("none", _store.Type("missing"));
    }
}
