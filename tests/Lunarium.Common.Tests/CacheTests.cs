using System.Collections.Concurrent;
using Lunarium.Common.Caching;

namespace Lunarium.Common.Tests;

public class CacheTests
{
    [Fact]
    public void SetGet_RoundTripsAllSupportedTypes()
    {
        var cache = new Cache();
        cache.Set("s", "文本值");
        cache.Set("i", 42);
        cache.Set("l", 9_000_000_000L);
        cache.Set("sh", (short)-7);
        cache.Set("ui", 42u);
        cache.Set("ul", ulong.MaxValue);
        cache.Set("d", 1.25d);
        cache.Set("f", 0.5f);
        cache.Set("b", true);

        Assert.Equal("文本值", cache.Get<string>("s"));
        Assert.Equal(42, cache.Get<int>("i"));
        Assert.Equal(9_000_000_000L, cache.Get<long>("l"));
        Assert.Equal((short)-7, cache.Get<short>("sh"));
        Assert.Equal(42u, cache.Get<uint>("ui"));
        Assert.Equal(ulong.MaxValue, cache.Get<ulong>("ul"));
        Assert.Equal(1.25d, cache.Get<double>("d"));
        Assert.Equal(0.5f, cache.Get<float>("f"));
        Assert.True(cache.Get<bool>("b"));
    }

    [Fact]
    public void Set_IntoLlmatchingBucket()
    {
        var cache = new Cache();
        cache.Set("s", "x");
        cache.Set("l", 1L);
        cache.Set("ul", 1UL);
        cache.Set("d", 1d);
        cache.Set("b", false);

        Assert.Single(cache.Strings);
        Assert.Single(cache.Numbers);
        Assert.Single(cache.UnsignedNumbers);
        Assert.Single(cache.FloatingNumbers);
        Assert.Single(cache.Booleans);
    }

    [Fact]
    public void Set_UnsupportedType_Throws()
    {
        var cache = new Cache();
        Assert.Throws<NotSupportedException>(() => cache.Set("dt", DateTime.UtcNow));
        Assert.Throws<NotSupportedException>(() => cache.Set("dec", 1.5m));
        Assert.Throws<NotSupportedException>(() => cache.Set("obj", new object()));
        Assert.Throws<NotSupportedException>(() => cache.Set("arr", new[] { 1, 2 }));
    }

    [Fact]
    public void Get_MissingKey_ReturnsDefaultOrProvidedFallback()
    {
        var cache = new Cache();
        Assert.Null(cache.Get<string>("nope"));
        Assert.Equal(0, cache.Get<int>("nope"));
        Assert.False(cache.Get<bool>("nope"));
        Assert.Equal(99, cache.Get("nope", 99));
        Assert.Equal("fallback", cache.Get("nope", "fallback"));
    }

    [Fact]
    public void Get_TypeMismatch_ReturnsDefault()
    {
        var cache = new Cache();
        cache.Set("s", "abc");

        Assert.Equal(0, cache.Get<int>("s"));               // 请求 long/int 与 String 桶不符
        Assert.Equal(-1, cache.Get("s", -1));
        Assert.Equal(0m, cache.Get<decimal>("s"));          // 不受支持的类型也视为未命中
    }

    [Fact]
    public void Set_SameKeyWithDifferentType_MovesBucketAndClearsOldValue()
    {
        var cache = new Cache();
        cache.Set("k", 1);
        Assert.Single(cache.Numbers);

        cache.Set("k", "现在是字符串");

        Assert.Equal("现在是字符串", cache.Get<string>("k"));
        Assert.Equal(0, cache.Get<int>("k"));       // 旧桶已清理，不再命中
        Assert.Empty(cache.Numbers);
        Assert.Single(cache.Strings);
    }

    [Fact]
    public void Remove_DeletesFromOwningBucketAndReportsHit()
    {
        var cache = new Cache();
        cache.Set("k", 5);

        Assert.True(cache.Remove("k"));
        Assert.False(cache.Remove("k"));            // 再次删除返回 false
        Assert.Empty(cache.Numbers);
        Assert.Equal(0, cache.Get<int>("k"));
    }

    [Fact]
    public void RebuildIndex_RestoresLookupAfterDirectBucketWrites()
    {
        // 模拟反序列化后的状态：桶里有数据但派生索引为空
        var cache = new Cache
        {
            Strings = new() { ["a"] = "1" },
            Numbers = new() { ["b"] = 2 }
        };

        Assert.Null(cache.Get<string>("a"));        // 索引未建 → 读不到

        cache.RebuildIndex();

        Assert.Equal("1", cache.Get<string>("a"));
        Assert.Equal(2L, cache.Get<long>("b"));
    }

    [Fact]
    public void RebuildIndex_DropsDuplicatesByBucketPriority()
    {
        var cache = new Cache
        {
            Strings = new() { ["dup"] = "winner" },
            Numbers = new() { ["dup"] = 1 },
            Booleans = new() { ["dup"] = true }
        };

        cache.RebuildIndex();

        Assert.Equal("winner", cache.Get<string>("dup"));   // String 优先级最高
        Assert.DoesNotContain("dup", cache.Numbers.Keys);
        Assert.DoesNotContain("dup", cache.Booleans.Keys);
    }

    [Fact]
    public void RebuildIndex_OnEmptyCache_IsNoOp()
    {
        var cache = new Cache();
        cache.RebuildIndex();
        Assert.Null(cache.Get<string>("any"));
    }

    [Fact]
    public void SetAndRemove_AreThreadSafe()
    {
        var cache = new Cache();

        Parallel.For(0, 5_000, i =>
        {
            cache.Set("k" + i, i);
            cache.Set("shared", i);
            _ = cache.Get<int>("shared");
            cache.Remove("k" + i);
        });

        Assert.Single(cache.Numbers);                       // 只剩 "shared"
        Assert.True(cache.Numbers.ContainsKey("shared"));
    }
}
