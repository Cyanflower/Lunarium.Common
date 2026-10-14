namespace Lunarium.Common.Tests;

/// <summary>
/// 测试辅助方法。
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// 生成一个唯一的缓存命名空间。CacheStore 内部用全局注册表占用命名空间，
    /// 且 xUnit 默认并行跑测试类，故每个测试都必须用独立命名空间避免互相冲突。
    /// </summary>
    public static string UniqueNamespace() => "t" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// 轮询等待条件成立，避免用固定 sleep 造成的偶发失败。
    /// </summary>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, int pollMilliseconds = 5)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(pollMilliseconds).ConfigureAwait(false);
        }
        return condition();
    }

    /// <summary>
    /// 创建一个唯一的临时目录，返回其路径；调用方负责删除。
    /// </summary>
    public static string NewTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lunarium-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
