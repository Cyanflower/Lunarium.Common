# Lunarium.Common 使用指南与模块索引

> 面向使用者的任务导向文档：**什么时候用哪个模块、怎么组合、哪些边界要注意**。
> 逐成员 API 速查见 [docs/api/](api/README.md)；本页侧重使用场景、组合方式与边界，签名以源码 XML 注释 / IDE IntelliSense 为准。
>
> 当前版本：1.4.x ｜ 目标框架：`net8.0` / `net9.0` / `net10.0` ｜ 根目录 [README.md](../README.md)

## 文档地图

| 位置 | 内容 |
| --- | --- |
| 本页 | 使用指南、模块索引、选型、横切约定、常见坑 |
| [`docs/api/`](api/README.md) | 按模块拆分的 API 速查 |
| [`docs/examples/`](examples/README.md) | 可运行的控制台示例 |
| [根 README](../README.md) | 项目简介、安装、快速上手、模块表、AOT 说明 |

## 目录

- [1. 这个库是什么](#what)
- [2. 安装与目标框架](#install)
- [3. 5 分钟上手](#quickstart)
- [4. 模块索引](#modules)
- [5. 按任务选型](#choose)
- [6. 横切约定](#conventions)
- [7. 常见坑](#pitfalls)
- [8. 版本与兼容性](#version)
- [9. API 文档与示例](#docs-plan)

<a id="what"></a>

## 1. 这个库是什么

`Lunarium.Common` 是 Lunarium 项目的公共类库，把跨项目复用的通用类型与工具代码放在**同一个程序集**里，按命名空间划分模块，不拆成多个 NuGet 包。

它提供的是小型基础能力：

- 常用字符集常量（`Alphabet`）
- 环境变量读取、写入与类型化解析（`EnvVar`）
- 代理 URL / 普通环境 URL（`ProxyUrl`、`EnvUrl`）
- 随机短 ID 与单机雪花 ID（`NanoId`、`LocalSnowflake`）
- 脏状态防抖与取消令牌防抖（`DirtyDebouncer`、`CtsDebouncer`）
- 带防抖落盘的标量 KV 缓存与单文档存储（`CacheStore`、`JsonDocumentStore<T>`）

它**不**提供：依赖注入容器、日志框架、数据库、分布式协调、通用 JSON 序列化封装。需要这些能力时，请组合对应的 BCL 或其他库；本库只负责自己的那部分。

设计上的几个前提：

- 零外部 NuGet 依赖，只使用 BCL。
- 开启 `IsAotCompatible` / `IsTrimmable`，可在 Native AOT 与裁剪应用中使用。
- 代码使用文件范围命名空间、可空引用类型与中文 XML 文档注释。
- 公开 API 以“小、直接、可组合”为主，不隐藏异常和资源释放责任。

<a id="install"></a>

## 2. 安装与目标框架

```bash
dotnet add package Lunarium.Common
```

目标框架：`net8.0`、`net9.0`、`net10.0`。

- `CtsDebouncer` 在 .NET 9 及以上使用 `System.Threading.Lock`；.NET 8 自动退回 `object` 加锁（条件编译）。
- 运行单元测试需要本机具备对应运行时；测试项目自身只面向 `net10.0`。
- 包内没有第三方依赖；AOT / trimming 相关分析器在普通 `dotnet build` 中就会生效。

<a id="quickstart"></a>

## 3. 5 分钟上手

### 3.1 读环境变量

```csharp
using Lunarium.Common;

string? port = EnvVar.Get("PORT")?.Value;          // 未设置或值为空白 → null
int timeout = EnvVar.GetRequired("TIMEOUT").As(30); // 缺失/空白 → InvalidOperationException
bool enabled = EnvVar.GetRequired("FEATURE_X").As<bool>(); // 宽松布尔字面量

EnvVar.Set("MODE", "production");
EnvVar.Unset("MODE");
```

要点：

- `Name` / `Value` 在构造时裁剪首尾空白；“未设置”与“值为空白”无法区分。
- `Set` 拒绝空白值；要清空变量只能用 `Unset`（彻底删除）。
- `ToString()` 只返回变量名，不返回值，避免日志泄露密钥；取值请用 `.Value`。
- 刻意不提供 `implicit operator string`，避免 `"" + envVar` 这类写法绕过脱敏。

### 3.2 生成 ID

```csharp
using Lunarium.Common.UID;

string requestId = NanoId.Generate();       // 默认 21 字符、URL 安全字符集
long eventId = LocalSnowflake.NextId();     // 64 位、进程内单调递增
DateTimeOffset createdAt = LocalSnowflake.ExtractTime(eventId);
```

选择建议：

- 只需要随机、不可预测的字符串 ID → `NanoId`。
- 需要 64 位整数、进程内可排序、能从 ID 解算时间 → `LocalSnowflake`。
- 需要严格的跨进程/跨机器唯一保证 → 两者都不提供硬保证：`NanoId` 靠随机性在实践中可用，`LocalSnowflake` 没有 node id，多进程可能重复。

### 3.3 持久化一个小缓存

```csharp
using Lunarium.Common.Caching;

// 显式目录：容器、systemd 只读 root、安装在 /usr/lib 等场景务必指定可写目录
await using var cache = new CacheStore("bot-state", storageDirectory: "/var/lib/lunarium");

cache.Set("restart-count", 3);
int count = cache.Get("restart-count", 0);

await cache.FlushAsync(); // 立即落盘；DisposeAsync 也会刷盘
```

`CacheStore` 的写盘是 5 秒防抖的：`Set` / `Remove` 先改内存并标脏，后台任务合并写入。进程崩溃可能丢掉上次成功落盘之后的修改（正常情况最多约 5 秒）；关键检查点请显式 `FlushAsync()`。

<a id="modules"></a>

## 4. 模块索引

| 模块 | 命名空间 | 公开类型 | 解决什么问题 | API |
| --- | --- | --- | --- | --- |
| `Alphabet` | `Lunarium.Common` | `Alphabet` | 常用字符集常量：数字、字母、Base 系列、防混淆、密码字符集 | [alphabet](api/alphabet.md) |
| `EnvVar` | `Lunarium.Common` | `EnvVar` | 环境变量包装：读取 / 写入 / 删除 + 类型化解析 | [env-var](api/env-var.md) |
| `Net` | `Lunarium.Common.Net` | `ProxyUrl`、`EnvUrl` | 代理配置与通用环境 URL，转为 `IWebProxy` / `Uri` | [net](api/net.md) |
| `UID` | `Lunarium.Common.UID` | `NanoId`、`LocalSnowflake` | 随机短 ID 与单机雪花 ID | [uid](api/uid.md) |
| `Debounce` | `Lunarium.Common.Debounce` | `Debouncer`、`DirtyDebouncer`、`CtsDebouncer` | 两种防抖模型：轮询脏标记 / 取消令牌尾沿防抖 | [debounce](api/debounce.md) |
| `Caching` | `Lunarium.Common.Caching` | `Cache`、`CacheStore`、`JsonDocumentStore<T>`、`CacheFileCorruptException` | 内存 KV + 防抖 JSON 落盘；单文档存储 | [caching](api/caching.md) |

补充说明：

- `Cache` 是公开的底层容器，通常应直接用 `CacheStore`。只有需要直接操作桶、或自行处理 JSON 时才用它；绕过 `Set<T>` 直接写桶后必须调用 `RebuildIndex()`。
- `CacheFileCorruptException` 用于区分“文件内容损坏”和普通 IO 故障，构造函数可能抛出它。
- `DirtyDebouncer` / `CtsDebouncer` 的配置是各自的嵌套 `Options` 记录。
- `Debouncer` 只是工厂类，实际实例是 `DirtyDebouncer` 或 `CtsDebouncer`。
- 类库在同一个程序集内，目录结构与命名空间对齐：`src/Lunarium.Common/<Module>/Xxx.cs` 对应 `namespace Lunarium.Common.<Module>;`。

可运行示例见 [`docs/examples/`](examples/README.md)，逐成员签名速查见 [`docs/api/`](api/README.md)。

<a id="choose"></a>

## 5. 按任务选型

### 5.1 环境变量、代理与 URL

| 需求 | 推荐 |
| --- | --- |
| 读普通环境变量 | `EnvVar.Get` / `GetRequired` |
| 多个候选变量名，按优先级 | `EnvVar.GetAny` / `GetAnyRequired` |
| 代理配置 | `ProxyUrl.FromEnvironment()` / `ProxyUrl.FromEnvironment("MY_PROXY")` |
| 普通环境 URL | `EnvUrl.FromEnvironment("API_BASE_URL")` |

```csharp
using System.Net;
using Lunarium.Common;
using Lunarium.Common.Net;

ProxyUrl? proxy = ProxyUrl.FromEnvironment(); // 默认依次读 http_proxy → HTTP_PROXY
IWebProxy? webProxy = proxy?.ToWebProxy();

EnvUrl? apiBase = EnvUrl.FromEnvironment("API_BASE_URL");
Uri? apiUri = apiBase?.ToUri();
```

语义要点：

- `ProxyUrl` 的默认变量名是 `http_proxy`、`HTTP_PROXY`（小写优先，第一个有效值生效）；也可以显式传变量名。
- `EnvUrl` 没有默认变量名，必须显式指定；它只表示“环境里的 URL”，不包含代理转换。
- `ProxyUrl` / `EnvUrl` 都提供 `ToString()` 与隐式 `string` 转换；直接 `new` 时只校验非空白，不校验 URI 格式，`ToUri()` 才可能抛 `UriFormatException`。
- 从环境变量创建时，值经 `EnvVar` 裁剪首尾空白；直接 `new` 不会额外裁剪。
- `EnvVar.As<T>()` 走 `IParsable<T>`（固定不变文化）。`T` 为 `bool` 时使用宽松字面量：`true/false`、`1/yes/on`、`0/no/off`。
- 枚举请用 `AsEnum` / `RequiredAsEnum`。注意默认值不同：`AsEnum` 默认 `requireDefined: false`（`"99"` 这类未定义纯数字也可能被接受），`RequiredAsEnum` 默认 `requireDefined: true`。

```csharp
DayOfWeek day = EnvVar.GetRequired("REPORT_DAY").AsEnum(DayOfWeek.Monday);
DayOfWeek safeDay = EnvVar.GetRequired("REPORT_DAY").RequiredAsEnum<DayOfWeek>(); // 拒绝未定义值
bool feature = EnvVar.GetRequired("FEATURE_X").AsBoolean("enabled", "disabled"); // 自定义字面量，大小写不敏感
```

### 5.2 ID：`NanoId` 还是 `LocalSnowflake`

| | `NanoId` | `LocalSnowflake` |
| --- | --- | --- |
| 输出 | 字符串 / UTF-8 字节 | 64 位 `long` |
| 排序 | 无 | 进程内单调递增 |
| 时间信息 | 无 | `ExtractTime` / `IsExpired` |
| 多进程 | 随机，无需协调 | 单进程；无 node id，多进程可能重复 |
| 适合 | 请求 ID、token、短码、验证码 | 单机事件 ID、需要按时间排序/过期判断 |

```csharp
using Lunarium.Common;
using Lunarium.Common.UID;

string token = NanoId.Generate();                       // 21 chars, UrlSafe
string code = NanoId.Generate(12, Alphabet.HumanReadable); // 12 chars, 防混淆
byte[] utf8 = NanoId.GenerateUtf8();                     // 直接写 UTF-8 字节
Span<byte> buffer = stackalloc byte[21];
NanoId.GenerateUtf8(buffer);                             // 零分配写入调用方缓冲区

long id = LocalSnowflake.NextId();
DateTimeOffset created = LocalSnowflake.ExtractTime(id);
bool expired = LocalSnowflake.IsExpired(id, TimeSpan.FromHours(24));
```

`LocalSnowflake` 的位布局是 `1 bit 符号 + 41 bits 毫秒时间戳（自 UTC 2026-01-01）+ 22 bits 序列号`：

- 线程安全、进程内单调递增；时钟回拨时借用逻辑时间，不会倒退。
- 没有机器/节点编号，只适合单机单进程。需要跨进程唯一时请改用其他方案或自行加节点前缀。
- 41 位时间戳从 2026-01-01 起约可用 69 年；同一毫秒最多约 419 万个序列号。
- `ExtractTime` 依赖上述纪元；不要拿它解析其他来源的雪花 ID。

`NanoId` 的字符集长度支持 1~256；`GenerateUtf8` 的重载要求字符集仅含 ASCII。默认 `Alphabet.UrlSafe` 是 64 字符，21 字符约 126 bit 熵。

### 5.3 防抖：`CtsDebouncer` 还是 `DirtyDebouncer`

| | `CtsDebouncer` | `DirtyDebouncer` |
| --- | --- | --- |
| 触发方式 | 每次 `Invoke()` | `MarkDirty()` 置脏 |
| 执行时机 | 最后一次 `Invoke` + `delay` | 下一个 `interval` 轮询点 |
| 合并方式 | 取消上一次延迟 | 脏标记合并 |
| 最长等待 | `Options.MaxWait` 可限制饥饿 | 固定轮询，无额外上限 |
| 失败处理 | 交给 `OnError`，不自动重试 | 重新置脏，下一轮重试 |
| 关闭行为 | `DisposeAsync` 等待已排队 action | `DisposeAsync` 最后提交脏数据 |
| 适合 | 搜索输入、窗口变化、频繁配置变更 | 周期落盘、批量脏状态 |

**尾沿防抖**：连续调用只触发一次，发生在最后一次调用之后。

```csharp
using Lunarium.Common.Debounce;

// SaveAsync 是你的保存逻辑；ct 是调用方提供的外部令牌
await using var debouncer = Debouncer.CreateCts(
    async ct => await SaveAsync(ct),
    TimeSpan.FromMilliseconds(500),
    new CtsDebouncer.Options { MaxWait = TimeSpan.FromSeconds(5) });

debouncer.Invoke();
```

**脏标记防抖**：按固定周期检查，有脏数据才执行。

```csharp
await using var saver = Debouncer.CreateDirty(
    async ct => await SaveAsync(ct),
    TimeSpan.FromSeconds(5));

saver.MarkDirty();
```

共同约定：

- action 串行执行，同一时刻最多一个；`ExecuteNowAsync()` 与定时触发的 action 也排队互斥。
- `ExecuteNowAsync()` 立即排队执行一次：`CtsDebouncer` 不会取消/重置当前待触发的延迟；`DirtyDebouncer` 成功则清除脏标记，失败则保留待下一轮重试。
- action 收到的取消令牌始终是调用方提供的 `ActionCancellationToken`；防抖器不会取消它。
- action 抛出的 `OperationCanceledException` 视为正常取消，不报告为错误；其他异常交给 `OnError`。
- `SafeOnError` 默认 `true`：吞掉 `OnError` 回调自身的异常，避免击穿后台任务。只有你确实能处理回调异常时才设为 `false`。
- 同步 `Dispose()` 只停止/取消，不等待最后一次落盘或在途 action；需要优雅收尾请用 `DisposeAsync()`。

### 5.4 持久化：`CacheStore` 还是 `JsonDocumentStore<T>`

| | `CacheStore` | `JsonDocumentStore<T>` |
| --- | --- | --- |
| 数据形态 | 标量键值对 | 单个强类型 JSON 文档 |
| 支持类型 | `string`、`int`、`long`、`short`、`uint`、`ulong`、`double`、`float`、`bool` | 任何能由 System.Text.Json 序列化的 `class, new()` |
| 业务校验 | 无 | 无，调用方负责 |
| 读取 | `Get<T>` | `Read()` 深拷贝 |
| 写入 | `Set<T>` / `Remove` | `Set` / `Update` |
| 序列化配置 | 库内源生成上下文，无需配置 | 调用方必须提供源生成的 `JsonTypeInfo<T>` |
| 默认落盘 | `{AppContext.BaseDirectory}/CacheStore/{cacheNamespace}.json` | `{AppContext.BaseDirectory}/JsonDocumentStore/{name}.json` |
| 适合 | 计数器、开关、小型状态（KB 级，≤ 8 MB） | 配置、状态文档、复杂对象树 |

**标量 KV：`CacheStore`**

```csharp
await using var cache = new CacheStore("bot-state", storageDirectory: "/var/lib/lunarium");

cache.Set("enabled", true);
cache.Set("limit", 10);
bool enabled = cache.Get("enabled", false);
cache.Remove("limit");

await cache.FlushAsync();
```

- 只支持上表中的标量类型；`DateTime`、`decimal`、枚举、`Guid` 等会抛 `NotSupportedException`。需要这些类型时用 `JsonDocumentStore<T>` 或自行转成 `string` / `long`。
- 适合 KB 级到不超过 8 MB 的轻量状态；不要拿它存大对象，也不要用作通用数据库。
- `Get<T>` 在键不存在或桶不匹配时返回默认值；不会抛“类型转换失败”。
- 写盘是 5 秒防抖；`FlushAsync()` 立即写，`DisposeAsync()` 会刷盘并释放文件路径占用。

**单文档：`JsonDocumentStore<T>`**

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

语义重点：

- **快照语义**：`Read` / `Set` / `Update` 都通过 JSON 往返做深拷贝。`Read` 返回的实例可以随意修改，不会影响存储；要持久化修改必须再 `Set` 或 `Update`。
- `[JsonIgnore]` 成员不参与快照与落盘；本存储以 JSON 中的内容为准。
- **不做业务校验**：校验是调用方策略。不要放在保存阶段——防抖落盘是后台任务，失败只能进 `logError`，调用方无法同步感知。
- `Update` 在锁内执行 `transform`，保持短小，不要在里面做 I/O；`transform` 抛异常或返回 `null` 时，存储内容保持不变。
- `T` 必须是 `class, new()`；反序列化依赖调用方提供的 `JsonTypeInfo<T>`，AOT 下不能走反射。
- 构造时文件不存在或为空白 → 使用 `new T()`；文件损坏 → 抛 `CacheFileCorruptException`，除非显式允许重建。
- 缩进、Encoder、注释处理等选项由传入的 `JsonTypeInfo<T>` 所属上下文决定。`[JsonSourceGenerationOptions]` 没有 `Encoder` 参数；需要非 ASCII 不转义时，构造上下文时传入静态 `JsonSerializerOptions`（见根 README 的 AOT 一节）。

**两者共同的路径与损坏处理**

- 显式传入 `storageDirectory` 时，该目录直接作为文件所在目录，不会再追加 `CacheStore` / `JsonDocumentStore` 子目录。
- 相对路径以 `AppContext.BaseDirectory` 为基准解析，不是进程当前目录；构造时立即转为绝对路径。
- `CacheStore` 与 `JsonDocumentStore<T>` 共享进程级文件路径注册表：同一个文件路径不允许被任一存储类型的两个实例同时占用，重复构造会抛 `InvalidOperationException`。释放实例后路径才会释放。
- 文件损坏时默认抛 `CacheFileCorruptException`。设置 `allowCorruptRebuild: true` 时必须同时提供 `logError`；库会记录异常、把损坏文件改名为 `{file}.corrupt-yyyyMMddHHmmss`（尽力而为），然后以空缓存/空文档继续。
- 容器、systemd 只读 root、安装在 `/usr/lib` 等不可写目录时，必须显式指定可写目录。示例中的 `/var/lib/lunarium` 只是 Linux 可写目录示例，按运行平台替换。

### 5.5 字符集：`Alphabet`

| 类别 | 常量 |
| --- | --- |
| 基础 | `Digits`、`Lowercase`、`Uppercase`、`Letters`、`Base` |
| 编码 | `HexLower`、`HexUpper`、`Base32`、`Base36`、`Base58`、`Base64`、`UrlSafe` |
| 防混淆 / 人类可读 | `CrockfordBase32`、`HumanReadable`、`DigitsNoAmbiguous` |
| 密码 | `Special`、`Complex` |

```csharp
string code = NanoId.Generate(12, Alphabet.HumanReadable); // 去掉 0/1/I/O 等易混字符
string id = NanoId.Generate(16, Alphabet.CrockfordBase32);
```

- `Base58` 已剔除 `0`、`O`、`I`、`l`；`CrockfordBase32` 排除 `I`、`L`、`O`、`U`；`HumanReadable` 排除 `0`、`1`、`I`、`O`。
- 这些常量都是 `string`，可直接传给 `NanoId.Generate` 或用于自己的随机生成逻辑；`NanoId` 只要求字符集长度 1~256，UTF-8 重载额外要求 ASCII。

<a id="conventions"></a>

## 6. 横切约定

### 6.1 释放与刷盘

`CacheStore`、`JsonDocumentStore<T>` 只实现 `IAsyncDisposable`，请使用 `await using`。`CtsDebouncer`、`DirtyDebouncer` 同时实现 `IDisposable` 与 `IAsyncDisposable`，同样优先使用 `await using`。

```csharp
await using var store = new CacheStore("state", storageDirectory: "/var/lib/lunarium");
// ...
```

- `FlushAsync()`：立即执行一次落盘，不等待防抖倒计时。
- 存储的 `DisposeAsync()`：停止后台任务，刷掉脏数据，等待在途 action，随后释放文件路径占用。
- 防抖器的 `DisposeAsync()`：停止/取消并等待已排队的 action；`DirtyDebouncer` 还会在退出前提交残留脏数据。
- 防抖器的同步 `Dispose()`：只发出停止/取消信号，**不保证**最后一次落盘或在途 action 完成。存储没有同步 `Dispose()`；需要持久化保证时用 `DisposeAsync()`。
- 释放后再调用对应的读写/刷盘方法（如 `CacheStore.Set/Get/Remove/FlushAsync`、`JsonDocumentStore<T>.Read/Set/Update/FlushAsync`）会抛 `ObjectDisposedException`；防抖器的 `Invoke` / `MarkDirty` / `ExecuteNowAsync` 同理。
- `CacheStore` / `JsonDocumentStore<T>` 的构造函数会同步加载文件，可能抛 `CacheFileCorruptException` 或其他加载异常（通常包装为 `InvalidOperationException`）；在启动路径上显式处理。

### 6.2 错误处理

- `CacheStore` / `JsonDocumentStore<T>` 的后台落盘错误通过构造参数 `logError` 报告。**不传 `logError` 时，后台写盘失败没有调用方可见的异常**；生产环境应提供日志回调。
- `DirtyDebouncer` / `CtsDebouncer` 的 action 异常通过 `Options.OnError` 报告；默认 `SafeOnError = true` 会吞掉回调自身异常。
- `allowCorruptRebuild: true` 必须同时提供 `logError`；否则构造时抛 `ArgumentNullException`，避免损坏文件被静默丢弃。
- 落盘是后台任务，`FlushAsync()` / `DisposeAsync()` 不应被当作业务异常通道；业务校验应在写入前完成。

<a id="aot"></a>

### 6.3 AOT / 裁剪

- 库自身已开启 `IsAotCompatible`，普通 `dotnet build` 就会启用 IL2026 / IL3050 分析器；CI 的构建步骤同时充当回归防线。
- `CacheStore` 使用库内 `CacheJsonContext` 源生成上下文，调用方不需要额外配置。
- `JsonDocumentStore<T>` 只使用 `JsonSerializer` 的 `JsonTypeInfo<T>` 重载；调用方必须提供源生成上下文产出的类型信息，例如 `MyJsonContext.Default.MyDocument`。
- 不要使用反射重载：

```csharp
// ✗ 挂 RequiresUnreferencedCode / RequiresDynamicCode，IL2026 / IL3050
JsonSerializer.Serialize(value, options);
JsonSerializer.Deserialize<T>(json, options);

// ✓ 走源生成的 JsonTypeInfo
JsonSerializer.Serialize(value, MyContext.Default.MyType);
JsonSerializer.Deserialize(json, MyContext.Default.MyType);
```

- 自定义类型需要被 `JsonDocumentStore<T>` 序列化时，必须在自己的 `JsonSerializerContext` 上登记 `[JsonSerializable(typeof(T))]`；忘记登记不会编译失败，但 AOT / 裁剪后会抛“反射序列化已禁用”。
- `Enum` 字符串互转、非源生成 `Regex`、`Activator.CreateInstance`、`Type.GetType`、`MakeGenericType`、`Expression` 等在 AOT 下也需谨慎；本库的 JSON 路径已避开反射序列化，其他细节以根 README 的 AOT 一节为准。

<a id="paths"></a>

### 6.4 文件路径与多实例

- 默认目录：`CacheStore` 为 `{AppContext.BaseDirectory}/CacheStore/`，`JsonDocumentStore<T>` 为 `{AppContext.BaseDirectory}/JsonDocumentStore/`。
- 显式目录：`storageDirectory: "/var/lib/lunarium"` → 文件直接落在 `/var/lib/lunarium/{name}.json`，不会追加子目录。
- 相对目录：以 `AppContext.BaseDirectory` 为基准，不是进程当前目录；构造时解析为绝对路径，之后 CWD 变化不影响落盘位置。
- 进程级注册表按解析后的完整路径去重；Windows 大小写不敏感，Linux 区分大小写。同一个文件不能被两个实例（包括 `CacheStore` 与 `JsonDocumentStore<T>` 各一个）同时占用。
- 损坏文件留档名：`{name}.json.corrupt-{yyyyMMddHHmmss}`（本地时间，尽力而为）。

### 6.5 线程安全

| 类型 | 说明 |
| --- | --- |
| `EnvVar` | 薄包装 `Environment`，不引入额外锁；进程环境的并发与大小写语义由平台决定 |
| `ProxyUrl` / `EnvUrl` | 不可变记录，可安全共享 |
| `NanoId` | 使用加密随机源，可并发调用 |
| `LocalSnowflake` | 无锁 CAS，线程安全；但只保证单进程内唯一 |
| `CacheStore` / `Cache` | 并发读写安全；`Set` / `Remove` 内部串行化索引与桶的更新 |
| `JsonDocumentStore<T>` | `Read` / `Update` 在锁内；`Set` 在锁外克隆后入账。`Update` 的 `transform` 在锁内执行，保持短小 |
| `CtsDebouncer` / `DirtyDebouncer` | action 串行；`Invoke` / `MarkDirty` 可并发调用，释放后调用会抛异常 |

### 6.6 目标框架差异

| 目标框架 | 说明 |
| --- | --- |
| `net8.0` | `CtsDebouncer` 使用 `object` + `Monitor` 兼容锁 |
| `net9.0` | `CtsDebouncer` 使用 `System.Threading.Lock` |
| `net10.0` | 同上；测试项目目标框架 |

<a id="pitfalls"></a>

## 7. 常见坑

1. **把 `EnvVar` 当字符串用**：`ToString()` 返回变量名，不是值；取值用 `.Value`。它也没有隐式 `string` 转换。
2. **想用空字符串表示“清空”**：`EnvVar` 的读取把空白视为未设置，`Set` 拒绝空白值；清空只能用 `Unset`。
3. **`AsEnum` 接受了未定义数字**：`AsEnum` 默认 `requireDefined: false`；需要拒绝 `"99"` 这类值时用 `RequiredAsEnum`（默认 `true`）。
4. **`As<bool>` 与 `bool.Parse` 不一致**：它接受 `1/yes/on`、`0/no/off`；需要自定义字面量时用 `AsBoolean`。
5. **拿 `LocalSnowflake` 做分布式 ID**：没有 node id，多进程可能重复；它只适合单机。
6. **往 `CacheStore` 里塞复杂类型**：只支持 9 种标量；`DateTime`、`decimal`、枚举、`Guid` 会抛 `NotSupportedException`。
7. **以为 `Set` 后立刻落盘**：`CacheStore` / `JsonDocumentStore<T>` 是 5 秒防抖；崩溃可能丢上次成功落盘之后的修改（正常最多约 5 秒），关键点用 `FlushAsync()`。
8. **用防抖器的同步 `Dispose()` 收尾**：它不等待最后一次落盘/在途 action；用 `DisposeAsync()`（存储没有同步 `Dispose()`）。
9. **修改 `Read()` 的返回值就以为持久化了**：`JsonDocumentStore<T>` 返回深拷贝；修改后要 `Set` / `Update`。
10. **忽略 `[JsonIgnore]`**：它不参与快照与落盘；不要指望这些成员能从文件恢复。
11. **AOT 下忘了登记类型**：`JsonDocumentStore<T>` 的 `T` 必须在调用方的 `JsonSerializerContext` 里登记；否则运行时才报反射序列化被禁用。
12. **同一个文件开两个实例**：`CacheStore` 与 `JsonDocumentStore<T>` 共享路径注册表；重复构造会抛 `InvalidOperationException`，释放后才可复用。
13. **在 `Update` 里做慢操作**：`transform` 在锁内执行，会阻塞 `Read` / `Set` / 落盘快照；保持短小。

<a id="version"></a>

## 8. 版本与兼容性

- 当前文档对应 `1.4.x`；目标框架为 `net8.0` / `net9.0` / `net10.0`。
- 文档随主分支更新，不做版本化副本；旧版本行为以对应 git tag 为准。
- 1.x 内以增量能力为主；升级前请查看 GitHub Releases 的发布说明。
- AOT / 裁剪兼容性、Symbol 包、SourceLink 等发布细节见根 [README.md](../README.md)。

<a id="docs-plan"></a>

## 9. API 文档与示例

- [`docs/api/`](api/README.md)：按模块拆分的 API 速查，包含签名、返回值、异常、线程安全与 AOT 注意事项。
- [`docs/examples/`](examples/README.md)：可运行的控制台示例，覆盖环境变量、ID、防抖、`CacheStore`、`JsonDocumentStore<T>`；已加入解决方案，CI 会编译并实际运行全部示例。
- 根 `README.md` 面向 NuGet 与首次访问，保留项目简介、安装、快速上手和模块表；更完整的选型、边界与示例放在本目录。与根 README 重复时，以本目录和源码 XML 注释为准。

## 许可证

本项目基于 [Apache License 2.0](../LICENSE) 授权，Copyright © 2026 Cyanflower。
