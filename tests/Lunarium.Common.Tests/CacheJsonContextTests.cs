using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lunarium.Common.Caching;

namespace Lunarium.Common.Tests;

/// <summary>
/// AOT / 裁剪回归防线：所有被序列化的类型都必须由源生成器静态产出元数据。
/// 一旦有人删掉 <see cref="CacheJsonContext"/> 上的登记，这里会立刻失败，
/// 而不是等到 AOT 应用运行时才抛 InvalidOperationException。
/// </summary>
public class CacheJsonContextTests
{
    [Fact]
    public void Context_ProvidesMetadataForCache()
    {
        JsonTypeInfo? info = CacheJsonContext.Default.GetTypeInfo(typeof(Cache));

        Assert.NotNull(info);
        Assert.Equal(JsonTypeInfoKind.Object, info!.Kind);
    }

    [Theory]
    [InlineData(typeof(ConcurrentDictionary<string, string>))]
    [InlineData(typeof(ConcurrentDictionary<string, long>))]
    [InlineData(typeof(ConcurrentDictionary<string, ulong>))]
    [InlineData(typeof(ConcurrentDictionary<string, double>))]
    [InlineData(typeof(ConcurrentDictionary<string, bool>))]
    public void Context_ProvidesMetadataForEveryBucketType(Type bucketType)
    {
        Assert.NotNull(CacheJsonContext.Default.GetTypeInfo(bucketType));
    }

    [Fact]
    public void Context_MetadataCoversExactlyThePersistedBuckets()
    {
        // 只应有 5 个桶属性；派生索引（_index）是私有字段，不应出现在元数据里
        string[] expected = ["Strings", "Numbers", "UnsignedNumbers", "FloatingNumbers", "Booleans"];
        string[] actual = CacheJsonContext.Default.Cache.Properties.Select(p => p.Name).ToArray();

        Assert.Equal(expected.OrderBy(n => n), actual.OrderBy(n => n));
    }

    [Fact]
    public void Serialize_ThroughSourceGeneratedTypeInfo_RoundTripsAllBuckets()
    {
        var cache = new Cache();
        cache.Set("s", "中文");
        cache.Set("n", 3);
        cache.Set("u", ulong.MaxValue);
        cache.Set("d", 0.5d);
        cache.Set("b", true);

        string json = JsonSerializer.Serialize(cache, CacheJsonContext.Default.Cache);
        var reloaded = JsonSerializer.Deserialize(json, CacheJsonContext.Default.Cache)!;
        reloaded.RebuildIndex();

        Assert.Equal("中文", reloaded.Get<string>("s"));
        Assert.Equal(3L, reloaded.Get<long>("n"));
        Assert.Equal(ulong.MaxValue, reloaded.Get<ulong>("u"));
        Assert.Equal(0.5d, reloaded.Get<double>("d"));
        Assert.True(reloaded.Get<bool>("b"));
    }
}
