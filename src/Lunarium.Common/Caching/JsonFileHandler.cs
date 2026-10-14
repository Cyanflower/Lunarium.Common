using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lunarium.Common.Caching;

internal sealed class JsonFileHandler(string filePath)
{
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    // 复用同一实例(JsonSerializerOptions 首次使用后会缓存元数据, 应共享复用)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // 格式化缩进, 便于人工排查
        WriteIndented = true,
        // 默认 Encoder 会把中文/URL 等非 ASCII 字符转义成 \uXXXX, 既膨胀文件又不利于人工排查
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // 源生成上下文与上面的 options 实例绑定: JsonSourceGenerationOptions 特性没有 Encoder 参数,
    // 非 ASCII 不转义的行为只能通过构造参数传入; 上下文按 options 实例缓存元数据, 故静态复用一个实例
    private static readonly CacheJsonContext JsonContext = new(JsonOptions);

    /// <summary>
    /// 保存缓存数据到文件(原子写: 先写临时文件再改名, 避免中途崩溃留下半截文件)
    /// </summary>
    /// <param name="cache">缓存数据</param>
    /// <param name="ct">用于取消写入操作的取消令牌</param>
    public async Task SaveToFileAsync(Cache cache, CancellationToken ct = default)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory); // 幂等, 已存在时为无操作

        // 内存序列化(放在锁外, 避免持锁做 CPU 工作)
        // 走源生成的 JsonTypeInfo 重载, 不用反射元数据; 泛型 + JsonSerializerOptions 的重载带
        // RequiresUnreferencedCode/RequiresDynamicCode, 即便配了 resolver 也仍会报 IL2026/IL3050
        string json = JsonSerializer.Serialize(cache, JsonContext.Cache);

        // 异步等待锁并异步落盘，完全不阻塞线程池线程
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 同目录临时文件 + 原子改名: 目标文件永远是完整内容, 不会出现半截写入
            string tempPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 从文件加载缓存数据。文件不存在或内容为空时返回空缓存；文件损坏时抛出异常。
    /// </summary>
    /// <exception cref="InvalidOperationException">文件存在但无法读取或解析时抛出。</exception>
    public Cache LoadFromFile()
    {
        if (!File.Exists(filePath))
            return new Cache();

        try
        {
            string json = File.ReadAllText(filePath);

            // 防止文件因意外为空时反序列化抛异常
            if (string.IsNullOrWhiteSpace(json))
                return new Cache();

            var cache = JsonSerializer.Deserialize(json, JsonContext.Cache) ?? new Cache();
            // 索引是私有派生状态, 不参与序列化 → 加载后重建, 保证 Get/Remove 定位正确
            cache.RebuildIndex();
            return cache;
        }
        catch (JsonException ex)
        {
            throw new CacheFileCorruptException($"加载缓存失败: JSON 格式错误 (文件: {filePath}) - {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载缓存失败 (文件: {filePath}) - {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 将当前(损坏的)缓存文件改名留档, 便于事后排查, 返回留档后的路径; 文件不存在时返回 <c>null</c>。
    /// </summary>
    public string? ArchiveCorruptFile()
    {
        if (!File.Exists(filePath))
            return null;

        string archivedPath = $"{filePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
        File.Move(filePath, archivedPath, overwrite: true);
        return archivedPath;
    }
}