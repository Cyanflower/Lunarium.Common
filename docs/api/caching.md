# Caching API

命名空间：`Lunarium.Common.Caching`

四个公开类型：

- `Cache`：标量 KV 的底层并发容器（通常不直接使用）。
- `CacheStore`：`Cache` + 5 秒防抖 JSON 落盘 + 文件路径占用。
- `JsonDocumentStore<T>`：单个强类型文档的快照 + 5 秒防抖 JSON 落盘。
- `CacheFileCorruptException`：文件存在但内容损坏时抛出的专用异常。

## Cache

```csharp
public class Cache
{
    public ConcurrentDictionary<string, string> Strings { get; set; }
    public ConcurrentDictionary<string, long> Numbers { get; set; }
    public ConcurrentDictionary<string, ulong> UnsignedNumbers { get; set; }
    public ConcurrentDictionary<string, double> FloatingNumbers { get; set; }
    public ConcurrentDictionary<string, bool> Booleans { get; set; }

    public void Set<T>(string key, T value);
    public T? Get<T>(string key, T? defaultValue = default);
    public bool Remove(string key);
    public void RebuildIndex();
}
```

- 5 个桶按运行时类型归类；一个键只能落在一个桶。`Set` 发现旧键在别的桶时会先清除旧值。
- 支持类型：`string`、`int`、`long`、`short`、`uint`、`ulong`、`double`、`float`、`bool`。
- `Set<T>` 遇到不支持的类型 → `NotSupportedException`。
- `Get<T>` 在键不存在或桶不匹配时返回 `defaultValue`，不会抛类型转换异常。
- `Remove` 命中返回 `true`，键不存在返回 `false`。
- 桶属性公开可写主要是为了 JSON 反序列化；绕过 `Set<T>` 直接写桶后，必须调用 `RebuildIndex()`，否则 `Get<T>` / `Remove` 无法定位。
- `RebuildIndex()` 的重复键优先级：`Strings` → `Numbers` → `UnsignedNumbers` → `FloatingNumbers` → `Booleans`；低优先级桶中的重复项会被清除。
- 私有索引不参与 JSON 序列化；反序列化加载后必须重建（`CacheStore` 内部已处理）。

`Cache` 本身不做落盘、防抖或路径管理；需要这些能力时用 `CacheStore`。

## CacheStore

```csharp
public sealed class CacheStore : IAsyncDisposable
{
    public CacheStore(
        string cacheNamespace,
        Action<Exception>? logError = null,
        bool allowCorruptRebuild = false,
        string? storageDirectory = null);

    public void Set<T>(string key, T value);
    public T? Get<T>(string key, T? defaultValue = default);
    public bool Remove(string key);
    public Task FlushAsync();
    public ValueTask DisposeAsync();
}
```

- 构造时同步加载 `{storageDirectory ?? AppContext.BaseDirectory/CacheStore}/{cacheNamespace}.json`，并启动 5 秒防抖落盘。
- `storageDirectory` 显式传入时直接使用该目录，不再追加 `CacheStore` 子目录；相对路径以 `AppContext.BaseDirectory` 为基准。
- 支持类型与 `Cache` 相同；`Set<T>` 不支持的类型 → `NotSupportedException`。
- 适合 KB 级到不超过 8 MB 的轻量状态；不要拿它存大对象，也不要用作通用数据库。
- `Get<T>` / `Remove` 的语义同 `Cache`；释放后调用抛 `ObjectDisposedException`。
- `FlushAsync()` 立即写入；`DisposeAsync()` 刷掉脏数据、等待在途 action、释放文件路径占用。
- 文件路径在进程内唯一：同一路径被其它实例（包括 `JsonDocumentStore<T>`）占用时，构造抛 `InvalidOperationException`。
- 文件不存在或内容为空白 → 空缓存；JSON 损坏 → `CacheFileCorruptException`；其它加载失败 → `InvalidOperationException`。
- `allowCorruptRebuild: true` 时必须提供 `logError`，否则抛 `ArgumentNullException`；允许重建时记录异常、尽力把损坏文件改名为 `{file}.corrupt-yyyyMMddHHmmss`，然后以空缓存继续。
- 后台落盘失败通过 `logError` 报告；不传时错误不可见。

构造参数异常：

| 场景 | 异常 |
| --- | --- |
| `cacheNamespace` 为 null/空 | `ArgumentNullException` / `ArgumentException` |
| `cacheNamespace` 含路径非法字符 | `ArgumentException` |
| `storageDirectory` 为空白字符串 | `ArgumentException` |
| `allowCorruptRebuild = true` 且 `logError == null` | `ArgumentNullException` |
| 文件路径已被占用 | `InvalidOperationException` |
| JSON 损坏且不允许重建 | `CacheFileCorruptException` |
| 文件读取等其它加载失败 | `InvalidOperationException` |

示例：

```csharp
using Lunarium.Common.Caching;

await using var cache = new CacheStore(
    "bot-state",
    logError: ex => Console.Error.WriteLine(ex),
    storageDirectory: "/var/lib/lunarium");

cache.Set("restart-count", 3);
cache.Set("enabled", true);
int count = cache.Get("restart-count", 0);
cache.Remove("enabled");

await cache.FlushAsync();
```

## JsonDocumentStore<T>

```csharp
public sealed class JsonDocumentStore<T> : IAsyncDisposable
    where T : class, new()
{
    public JsonDocumentStore(
        string documentName,
        JsonTypeInfo<T> typeInfo,
        Action<Exception>? logError = null,
        bool allowCorruptRebuild = false,
        string? storageDirectory = null);

    public T Read();
    public void Set(T value);
    public void Update(Func<T, T> transform);
    public Task FlushAsync();
    public ValueTask DisposeAsync();
}
```

- `T` 必须是 `class, new()`；`typeInfo` 必须来自源生成上下文（如 `MyJsonContext.Default.MyDocument`），同时决定缩进、Encoder、注释处理等选项。
- 持久化的是一个完整文档，不提供键值访问，也不做业务校验。
- **快照语义**：`Read` / `Set` / `Update` 都通过 JSON 往返深拷贝。`Read()` 返回独立副本；`Set` 保存调用方实例的副本；`Update` 在当前副本上执行变换后原子替换。
- `[JsonIgnore]` 成员不参与快照与落盘。
- `Update` 的 `transform` 在锁内同步执行，保持短小；抛异常或返回 `null` 时存储内容保持不变。
- `Set` 先克隆再入账；克隆失败时存储内容保持不变。
- 构造时文件不存在或为空白 → `new T()`；JSON 损坏或根节点为 `null` → `CacheFileCorruptException`；其它读取失败 → `InvalidOperationException`。
- `allowCorruptRebuild: true` 时必须提供 `logError`；允许重建时记录异常、尽力留档，然后以 `new T()` 继续。
- 默认路径：`{storageDirectory ?? AppContext.BaseDirectory/JsonDocumentStore}/{documentName}.json`；显式目录不再追加子目录。
- 与 `CacheStore` 共享进程级路径注册表；同一文件路径不能被任一存储类型的两个实例同时占用。
- 后台落盘失败通过 `logError` 报告；不传时错误不可见。

成员异常：

| 成员 | 异常 |
| --- | --- |
| 构造函数 | `ArgumentException` / `ArgumentNullException` / `InvalidOperationException` / `CacheFileCorruptException` |
| `Read()` | `ObjectDisposedException`；快照克隆失败时也可能抛 `JsonException` |
| `Set(T)` | `ArgumentNullException`、`ObjectDisposedException`、`JsonException`（克隆失败） |
| `Update(Func<T, T>)` | `ArgumentNullException`、`InvalidOperationException`（`transform` 返回 null）、`ObjectDisposedException`、`JsonException` |
| `FlushAsync()` | `ObjectDisposedException` |

示例：

```csharp
using System.Text.Json.Serialization;
using Lunarium.Common.Caching;

public sealed class BotConfig
{
    public string Name { get; set; } = "";
    public int RetryCount { get; set; } = 3;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BotConfig))]
internal partial class BotConfigJsonContext : JsonSerializerContext;

await using var store = new JsonDocumentStore<BotConfig>(
    "bot-config",
    BotConfigJsonContext.Default.BotConfig,
    storageDirectory: "/var/lib/lunarium");

store.Update(config =>
{
    config.RetryCount = 5;
    return config;
});

await store.FlushAsync();
```

## CacheFileCorruptException

```csharp
public sealed class CacheFileCorruptException : InvalidOperationException
{
    public CacheFileCorruptException(string message, Exception innerException);
}
```

- 表示“文件存在但内容无法解析”；用于区分“文件损坏”和普通 IO/权限失败。
- `CacheStore` 在 JSON 格式错误时抛出；`JsonDocumentStore<T>` 在 JSON 格式错误或根节点为 `null` 时抛出。
- 文件不存在、内容为空白不算损坏：存储会以空缓存/`new T()` 启动。
- 捕获后可以修正/删除文件再重试；也可以设置 `allowCorruptRebuild: true` + `logError` 让库自动留档并重建。

## 路径、损坏与错误处理

| 行为 | `CacheStore` | `JsonDocumentStore<T>` |
| --- | --- | --- |
| 默认目录 | `{AppContext.BaseDirectory}/CacheStore/` | `{AppContext.BaseDirectory}/JsonDocumentStore/` |
| 显式目录 | 直接使用，不追加子目录 | 直接使用，不追加子目录 |
| 相对目录 | 以 `AppContext.BaseDirectory` 为基准 | 以 `AppContext.BaseDirectory` 为基准 |
| 路径唯一性 | 进程级注册表，与另一类型共享 | 进程级注册表，与另一类型共享 |
| 损坏留档 | `{file}.corrupt-yyyyMMddHHmmss` | `{file}.corrupt-yyyyMMddHHmmss` |
| 5 秒防抖 | 是 | 是 |
| 后台错误 | `logError` | `logError` |

- 同一路径不能同时被两个实例占用；释放实例后路径才会释放。
- 容器、systemd 只读 root、安装在 `/usr/lib` 等场景必须显式指定可写目录。
- 关键检查点请调用 `FlushAsync()`；进程崩溃可能丢掉上次成功落盘之后的修改（正常最多约 5 秒）。
- 不要用 `FlushAsync()` / `DisposeAsync()` 做业务异常通道；业务校验应在 `Set` / `Update` 前完成。

## 线程安全 / AOT

- `Cache` / `CacheStore` 的并发读写安全；`Set` / `Remove` 内部串行化索引与桶更新。
- `JsonDocumentStore<T>` 的 `Read` / `Set` / `Update` 由锁保护；`Update` 的 `transform` 在锁内执行。
- `CacheStore` 使用库内源生成 `CacheJsonContext`；`JsonDocumentStore<T>` 使用调用方提供的 `JsonTypeInfo<T>`，不触发反射路径。
- `JsonDocumentStore<T>` 的 `T` 必须在调用方的 `JsonSerializerContext` 中登记；否则 AOT / 裁剪后运行时才会报反射序列化被禁用。

更完整的任务选型与边界说明见[使用指南](../README.md)。

可运行示例：[CacheExample.cs](../examples/CacheExample.cs)、[DocumentExample.cs](../examples/DocumentExample.cs)。
