using System.Collections.Concurrent;

namespace Lunarium.Common.Caching;

/// <summary>
/// 进程级缓存文件路径注册表：以解析后的完整文件路径为键，防止多个实例写同一个文件。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CacheStore"/> 与 <see cref="JsonDocumentStore{T}"/> 共享同一张表。若各自维护一份，
/// 两个不同类型的实例会同时占用同一个 <c>{name}.json</c>，用各自的数据格式互相覆盖。
/// </para>
/// <para>
/// Windows 文件系统大小写不敏感，使用 <see cref="StringComparer.OrdinalIgnoreCase"/> 避免大小写不同的同一路径被放行。
/// </para>
/// </remarks>
internal static class CacheFileRegistry
{
    private static readonly ConcurrentDictionary<string, byte> Paths =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// 解析存储目录：相对路径以 <see cref="AppContext.BaseDirectory"/> 为基准（不是进程当前目录），
    /// 并去掉尾部分隔符，保证 <c>"dir"</c>、<c>"dir/"</c> 等写法得到同一个去重键。
    /// </summary>
    /// <param name="storageDirectory">调用方提供的存储目录。</param>
    /// <returns>解析后的绝对目录路径。</returns>
    public static string ResolveDirectory(string storageDirectory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageDirectory, AppContext.BaseDirectory));

    /// <summary>
    /// 解析缓存文件的完整路径：<c>{storageDirectory ?? AppContext.BaseDirectory/defaultDirectoryName}/{name}.json</c>。
    /// 显式传入目录时直接使用该目录，不会再追加 <paramref name="defaultDirectoryName"/> 子目录。
    /// </summary>
    /// <param name="name">缓存/文档名称，不含扩展名。</param>
    /// <param name="defaultDirectoryName">未显式指定目录时使用的缺省子目录名。</param>
    /// <param name="storageDirectory">显式指定的存储目录，<c>null</c> 表示使用缺省位置。</param>
    /// <returns>解析后的绝对文件路径。</returns>
    public static string ResolveFile(string name, string defaultDirectoryName, string? storageDirectory) =>
        Path.Combine(
            storageDirectory is null
                ? Path.Combine(AppContext.BaseDirectory, defaultDirectoryName)
                : ResolveDirectory(storageDirectory),
            $"{name}.json");

    /// <summary>
    /// 尝试占用文件路径；已被其他实例占用时返回 <see langword="false"/>。
    /// </summary>
    /// <param name="filePath">解析后的完整文件路径。</param>
    /// <returns>成功占用返回 <see langword="true"/>。</returns>
    public static bool TryAcquire(string filePath) => Paths.TryAdd(filePath, 0);

    /// <summary>
    /// 释放文件路径占用（幂等）。
    /// </summary>
    /// <param name="filePath">解析后的完整文件路径。</param>
    public static void Release(string filePath) => Paths.TryRemove(filePath, out _);
}
