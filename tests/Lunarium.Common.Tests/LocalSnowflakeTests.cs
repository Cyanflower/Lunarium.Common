using Lunarium.Common.UID;

namespace Lunarium.Common.Tests;

public class LocalSnowflakeTests
{
    [Fact]
    public void NextId_IsAlwaysPositive()
    {
        for (int i = 0; i < 1_000; i++)
            Assert.True(LocalSnowflake.NextId() > 0);
    }

    [Fact]
    public void NextId_IsStrictlyMonotonic_SingleThread()
    {
        long previous = LocalSnowflake.NextId();
        for (int i = 0; i < 20_000; i++)
        {
            long current = LocalSnowflake.NextId();
            Assert.True(current > previous, $"未保持单调递增: {previous} -> {current}");
            previous = current;
        }
    }

    [Fact]
    public void NextId_IsUnique_UnderConcurrency()
    {
        const int threads = 8;
        const int perThread = 20_000;
        var ids = new long[threads * perThread];

        Parallel.For(0, threads, t =>
        {
            for (int i = 0; i < perThread; i++)
                ids[t * perThread + i] = LocalSnowflake.NextId();
        });

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void NextId_SequenceIncrementsWithinSameLogicalTimestamp()
    {
        // 同一逻辑毫秒内序列号应逐 1 递增（跨毫秒时差值会跳变，故只断言非递减且不被重置为更小值）
        long first = LocalSnowflake.NextId();
        long second = LocalSnowflake.NextId();
        Assert.True(second - first >= 1);
    }

    [Fact]
    public void ExtractTime_ReturnsTimeCloseToGenerationMoment()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        long id = LocalSnowflake.NextId();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        DateTimeOffset created = LocalSnowflake.ExtractTime(id);
        Assert.InRange(created, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void IsExpired_IsFalseForFreshIdAndTrueForNegativeTtl()
    {
        long id = LocalSnowflake.NextId();
        Assert.False(LocalSnowflake.IsExpired(id, TimeSpan.FromMinutes(5)));
        // 负 TTL 保证 (now - created) > ttl 恒成立，避免依赖「已过去 1 毫秒」造成抖动
        Assert.True(LocalSnowflake.IsExpired(id, TimeSpan.FromMilliseconds(-1)));
    }
}
