using Lunarium.Common.Caching;

namespace Lunarium.Common.Tests;

/// <summary>
/// 测试专用的临时存储根目录，作用域结束时自动递归删除。
/// </summary>
/// <remarks>
/// 注入给 <see cref="CacheStore"/> 的 <c>storageDirectory</c>，使测试完全隔离：
/// 既不必知道库内的落盘布局，也不会往 bin 目录里写文件，更不需要按前缀清理的收尾逻辑。
/// </remarks>
internal sealed class TempStorage : IDisposable
{
    public TempStorage() => Root = TestHelpers.NewTempDirectory();

    /// <summary>传给 <see cref="CacheStore"/> 的存储根目录。</summary>
    public string Root { get; }

    /// <summary>
    /// 某个命名空间的缓存文件路径：显式传入的存储目录会被直接使用，文件名为 <c>{cacheNamespace}.json</c>。
    /// </summary>
    public string FileFor(string cacheNamespace) => Path.Combine(Root, cacheNamespace + ".json");

    /// <summary>缓存文件所在目录。</summary>
    public string Directory => Root;

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不应影响测试结论
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
