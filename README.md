# Lunarium.Common

Lunarium 项目的公共类库（shared class library），用于存放跨项目复用的通用类型与工具代码。

> 完整文档：[使用指南与模块索引](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/README.md) ｜ [API 速查](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/api/README.md) ｜ [可运行示例](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/examples/README.md)

## 环境要求

- .NET SDK 10.0 或更高版本（可同时构建 net8.0 / net9.0 目标）
- 目标框架：`net8.0`、`net9.0`、`net10.0`（多目标）
  - .NET 9 及以上使用 `System.Threading.Lock`，.NET 8 自动退回 `object` 加锁（条件编译）
  - 运行单元测试需本机具备对应运行时（测试项目自身仅 `net10.0`）

## 项目结构

```
Lunarium.Common/
├── src/Lunarium.Common/        # 目录名 = 程序集名；tests/ 与之并列
│   ├── Lunarium.Common.csproj  # 类库项目文件（默认 glob 包含本目录下全部 .cs）
│   ├── Alphabet.cs             # Lunarium.Common
│   ├── EnvVar.cs               # Lunarium.Common
│   ├── Net/                    # Lunarium.Common.Net
│   ├── UID/                    # Lunarium.Common.UID
│   ├── Debounce/               # Lunarium.Common.Debounce
│   └── Caching/                # Lunarium.Common.Caching（含 CacheJsonContext 源生成上下文）
├── tests/Lunarium.Common.Tests/ # xUnit 单元测试（靠 InternalsVisibleTo 直接测 internal 类型）
├── docs/                       # 使用指南、API 速查、可运行示例
│   ├── README.md               # 使用指南与模块索引
│   ├── api/                    # 按模块拆分的 API 参考
│   └── examples/               # 可运行控制台示例（已加入 .slnx）
├── Lunarium.Common.slnx        # 解决方案文件（.NET 10 新格式，含 /src/、/tests/、/docs/ 解决方案文件夹）
├── .gitignore                  # 标准 .NET 忽略规则
├── LICENSE                     # Apache License 2.0
└── README.md
```

### 模块

| 模块 | 命名空间 | 说明 |
| --- | --- | --- |
| `Alphabet` | `Lunarium.Common` | 常用字符集常量（Base16/32/36/62、防混淆、密码字符集等） |
| `EnvVar` | `Lunarium.Common` | 环境变量包装：读取 / 写入 / 删除 + 类型化解析（`As<T>`、`AsEnum`、宽松布尔字面量） |
| `Net` | `Lunarium.Common.Net` | 代理配置 `ProxyUrl`（转换为 `IWebProxy` / `Uri`）；通用环境 URL `EnvUrl`（转换为 `Uri`） |
| `UID` | `Lunarium.Common.UID` | `NanoId` 无取模偏差 ID 生成（可直接输出 UTF-8 字节）、`LocalSnowflake` 无锁单机雪花 ID |
| `Debounce` | `Lunarium.Common.Debounce` | 脏状态防抖 `DirtyDebouncer` 与取消令牌防抖 `CtsDebouncer` |
| `Caching` | `Lunarium.Common.Caching` | 并发内存 KV + 防抖异步落盘（`CacheStore`）；单文档泛型存储（`JsonDocumentStore<T>`） |

所有模块均为同一程序集内的并列工具集，零外部 NuGet 依赖（仅使用 BCL）。**不拆分独立项目**——
仅当某模块需引入其他模块无法接受的重依赖、或需独立发版时才考虑拆分。

## 快速上手

完整的使用指南、模块索引、选型与边界见 [docs/README.md](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/README.md)；逐成员 API 见 [docs/api/](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/api/README.md)，可运行示例见 [docs/examples/](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/examples/README.md)。

### 环境变量

```csharp
using Lunarium.Common;

string? port = EnvVar.Get("PORT")?.Value;
int timeout = EnvVar.GetRequired("TIMEOUT").As(30);
bool enabled = EnvVar.GetRequired("FEATURE_X").As<bool>();

EnvVar.Set("MODE", "production");
EnvVar.Unset("MODE");
```

`EnvVar` 会裁剪名称/值、拒绝空白值，并让 `ToString()` 只返回变量名；代理/URL 与完整解析语义见[使用指南](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/README.md)。

### ID

```csharp
using Lunarium.Common.UID;

string requestId = NanoId.Generate();
long eventId = LocalSnowflake.NextId();
DateTimeOffset createdAt = LocalSnowflake.ExtractTime(eventId);
```

`NanoId` 适合随机 ID；`LocalSnowflake` 是单机雪花 ID，没有 node id，不能直接用于多进程/多机器全局唯一。

### 缓存

```csharp
using Lunarium.Common.Caching;

await using var cache = new CacheStore("bot-state", storageDirectory: "/var/lib/lunarium");
cache.Set("restart-count", 3);
int count = cache.Get("restart-count", 0);
await cache.FlushAsync();
```

`CacheStore` 用于标量 KV + 5 秒防抖落盘；需要持久化单个强类型文档时用 `JsonDocumentStore<T>`（要求调用方提供源生成的 `JsonTypeInfo<T>`）。落盘路径、损坏重建、快照语义与选型见[使用指南](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/README.md)。

## 构建与测试

```bash
# 在仓库根目录执行（根目录的 .slnx 会定位到 src、tests 与 docs/examples）

# 还原依赖
dotnet restore

# 生成（同时编译类库、测试与文档示例）
dotnet build

# 运行测试（测试项目目标框架为 net10.0）
dotnet test -c Release

# 只构建类库
dotnet build src/Lunarium.Common/Lunarium.Common.csproj

# 运行文档示例
dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- all

# 打包（同时产出 .nupkg 与 .snupkg 符号包）
dotnet pack src/Lunarium.Common/Lunarium.Common.csproj -c Release
```

## AOT / 裁剪兼容

完整的 AOT / 裁剪说明见[使用指南的 AOT 一节](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/README.md#aot)；下面保留发布与维护时最关键的约束。

本库已开启 `<IsAotCompatible>true</IsAotCompatible>`，可以安全用于 **Native AOT**（`PublishAot`）
与 **trimming**（`PublishTrimmed`）应用。

已实测：以 `PublishAot=true` 发布（`dotnet publish -c Release -r linux-x64`）得到原生可执行文件并可正常运行，
`Cache` / `CacheStore` 的序列化与落盘重载、`NanoId`、`LocalSnowflake`、`DirtyDebouncer` / `CtsDebouncer` 均无异常。

### 新增需要 JSON 序列化的类型时必须同步登记

`Cache` 的 JSON 元数据由源生成器在编译期静态产出（见 `Caching/CacheJsonContext.cs`）。
**任何库内需要被 `JsonSerializer` 序列化 / 反序列化的类型，都必须登记到 `CacheJsonContext`：**

```csharp
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Cache))]
[JsonSerializable(typeof(新类型))]   // ← 忘登记不会编译失败，只会在 AOT / 裁剪后运行时才报错
internal partial class CacheJsonContext : JsonSerializerContext;
```

> `JsonDocumentStore<T>` 不在此列：文档根类型位于消费方程序集，由调用方自己的 `JsonSerializerContext` 提供
> `JsonTypeInfo<T>`（见[使用指南](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/README.md)），库内不需要也无法登记。

忘记登记属于「编译期无感、运行时炸」的静默失败：库本身编译通过、无任何告警，
但 AOT / 裁剪后的应用会抛
`InvalidOperationException: Reflection-based serialization has been disabled for this application.`。

### 禁止的写法

```csharp
// ✗ 这两个泛型重载挂了 RequiresUnreferencedCode / RequiresDynamicCode，
//   即使给 JsonSerializerOptions 配了 TypeInfoResolver，IL2026 / IL3050 也照样报
JsonSerializer.Serialize(value, options);
JsonSerializer.Deserialize<T>(json, options);

// ✓ 只能走源生成的 JsonTypeInfo
JsonSerializer.Serialize(value, JsonContext.Cache);
JsonSerializer.Deserialize(json, JsonContext.Cache);
```

其他同类禁忌：`Activator.CreateInstance`、`Type.GetType`、`MakeGenericType`、`Expression` /
`DynamicMethod`、`Assembly.Load`；`Enum` 的字符串互转与 `Regex`（非源生成）在 AOT 下也需格外小心。

### 分析器是常驻的

`IsAotCompatible=true` 会隐含 `IsTrimmable=true`，并在**每次** `dotnet build`（含 IDE）中启用
IL2026 / IL3050 等分析器，因此上面那些写法在写下的当次构建就会被拦下：

```bash
# 无需任何额外参数，本库自检就是普通构建
dotnet build
```

同时程序集会带上 `IsTrimmable` / `IsAotCompatible` 元数据，消费者裁剪本库时不会产生额外告警。
注意该设置只开启分析器与标记，**不会**让库自身被 AOT 编译（类库不支持 `PublishAot`），
真正的 AOT 编译只由最终的可执行项目触发。

测试项目另通过 `<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>`
关闭了 System.Text.Json 的反射回退；`CacheStore` 与 `JsonDocumentStore<T>` 的全部读写测试都在该模式下运行。
一旦误用反射序列化，测试会立即失败，而不是等到 AOT 运行时才抛
`Reflection-based serialization has been disabled for this application.`。

### Encoder 需经由 options 实例传递（易踩）

`[JsonSourceGenerationOptions]` **没有** `Encoder` 参数（写了会 `CS0246`）；
「非 ASCII 不转义」只能通过构造上下文时传入的 `JsonSerializerOptions` 实例实现，
且该实例必须静态复用（上下文按 options 实例缓存元数据，首次使用后不可再修改）：

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

private static readonly CacheJsonContext JsonContext = new(JsonOptions);
```

## 约定

- 启用 `ImplicitUsings` 与 `Nullable`（见 `Lunarium.Common.csproj`）。
- 命名空间根为 `Lunarium.Common`，子模块使用 `Lunarium.Common.<Module>`；**目录结构与命名空间对齐**，
  即 `src/Lunarium.Common/<Module>/Xxx.cs` 对应 `namespace Lunarium.Common.<Module>;`。
- 新增模块时只在 `src/Lunarium.Common/` 下建一个同名目录，不新建项目。
- 代码统一使用文件范围命名空间（file-scoped namespace）与中文 XML 文档注释。
- **命名空间不得与其中的类型同名**（例如曾经的 `Lunarium.Common.CacheStore` 命名空间 + `CacheStore` 类型）。
  否则任何位于 `Lunarium.Common.*` 之下的代码引用该类型时会报 `CS0118: 'X' is a namespace but is used like a type`：
  简单名查找会自内向外逐层查找命名空间成员，在 `Lunarium.Common` 这层先撞上同名命名空间后
  就不再继续看 `using` 导入的同名类型了。该模块命名空间因此改为 `Lunarium.Common.Caching`。
- 改动涉及 JSON 序列化时，先读上面的「AOT / 裁剪兼容」一节。
- 涉及落盘路径时，先读[使用指南的「文件路径与多实例」](https://github.com/Cyanflower/Lunarium.Common/blob/main/docs/README.md#paths)：路径约定只在库内维护一处，测试一律注入临时目录。
- 修改公开 API 时同步更新 `docs/api/`；修改文档示例后运行 `dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- all`。

## 许可证

本项目基于 [Apache License 2.0](https://github.com/Cyanflower/Lunarium.Common/blob/main/LICENSE) 授权，Copyright © 2026 Cyanflower。

在遵守许可条款的前提下，可用于商业与非商业项目；再分发时请保留 `LICENSE` 与本文件的版权声明。
