using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Lunarium.Common.Caching;

/// <summary>
/// <see cref="JsonDocumentStore{T}"/> 的 JSON 文件读写器：加载时把整份文件反序列化为文档，
/// 保存时接收调用方在锁内生成好的 UTF-8 快照（保证写盘内容与内存状态一致）。
/// </summary>
/// <typeparam name="T">文档根类型。</typeparam>
internal sealed class JsonDocumentFileHandler<T>(string filePath, JsonTypeInfo<T> typeInfo) where T : class, new()
{
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <summary>
    /// 从文件加载文档。文件不存在或内容为空时返回新实例；文件损坏时抛出 <see cref="CacheFileCorruptException"/>。
    /// </summary>
    /// <returns>反序列化得到的文档。</returns>
    /// <exception cref="InvalidOperationException">文件存在但无法读取时抛出。</exception>
    /// <exception cref="CacheFileCorruptException">文件存在但内容不是合法 JSON，或根节点为 <c>null</c> 时抛出。</exception>
    public T LoadFromFile()
    {
        if (!File.Exists(filePath))
            return new T();

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载 JSON 文档失败 (文件: {filePath}) - {ex.Message}", ex);
        }

        // 防止文件因意外为空时反序列化抛异常
        if (string.IsNullOrWhiteSpace(json))
            return new T();

        try
        {
            // 走 TypeInfo 重载: 元数据由调用方的源生成上下文提供, 不触发反射路径
            return JsonSerializer.Deserialize(json, typeInfo)
                ?? throw new CacheFileCorruptException(
                    $"加载 JSON 文档失败: 根节点为 null (文件: {filePath})",
                    new JsonException("文档根节点为 null"));
        }
        catch (JsonException ex)
        {
            throw new CacheFileCorruptException($"加载 JSON 文档失败: JSON 格式错误 (文件: {filePath}) - {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 把调用方生成好的 UTF-8 JSON 快照原子写入文件。
    /// </summary>
    /// <param name="utf8Json">调用方在锁内序列化得到的完整 JSON 字节。</param>
    /// <param name="ct">用于取消写入操作的取消令牌。</param>
    public async Task SaveToFileAsync(byte[] utf8Json, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await JsonFileIO.WriteAllBytesAsync(filePath, utf8Json, ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 将当前(损坏的)文档文件改名留档, 返回留档后的路径; 文件不存在时返回 <c>null</c>。
    /// </summary>
    public string? ArchiveCorruptFile() => JsonFileIO.ArchiveCorrupt(filePath);
}
