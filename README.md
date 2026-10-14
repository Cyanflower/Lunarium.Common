# Lunarium.Common

Lunarium 项目的公共类库（shared class library），用于存放跨项目复用的通用类型与工具代码。

## 环境要求

- .NET SDK 10.0 或更高版本
- 目标框架：`net10.0`

## 项目结构

```
Lunarium.Common/
├── src/Lunarium.Common/        # 目录名 = 程序集名；tests/ 与之并列
│   ├── Lunarium.Common.csproj  # 类库项目文件（默认 glob 包含本目录下全部 .cs）
│   ├── Alphabet.cs             # Lunarium.Common
│   ├── UID/                    # Lunarium.Common.UID
│   ├── Debounce/               # Lunarium.Common.Debounce
│   └── Caching/                # Lunarium.Common.Caching（含 CacheJsonContext 源生成上下文）
├── tests/Lunarium.Common.Tests/ # xUnit 单元测试（靠 InternalsVisibleTo 直接测 internal 类型）
├── Lunarium.Common.slnx        # 解决方案文件（.NET 10 新格式，含 /src/ 与 /tests/ 解决方案文件夹）
├── .gitignore                  # 标准 .NET 忽略规则
├── LICENSE                     # Apache License 2.0
└── README.md
```

### 模块

| 模块 | 命名空间 | 说明 |
| --- | --- | --- |
| `Alphabet` | `Lunarium.Common` | 常用字符集常量（Base16/32/36/62、防混淆、密码字符集等） |
| `UID` | `Lunarium.Common.UID` | `NanoID` 无取模偏差 ID 生成、`LocalSnowflake` 无锁单机雪花 ID |
| `Debounce` | `Lunarium.Common.Debounce` | 脏状态防抖 `DirtyDebouncer` 与取消令牌防抖 `CtsDebouncer` |
| `Caching` | `Lunarium.Common.Caching` | 并发内存 KV + 防抖异步落盘的强类型缓存存储（`CacheStore`） |

所有模块均为同一程序集内的并列工具集，零外部 NuGet 依赖（仅使用 BCL）。**不拆分独立项目**——
仅当某模块需引入其他模块无法接受的重依赖、或需独立发版时才考虑拆分。

### 缓存落盘位置

`CacheStore` 默认把数据写在 `{AppContext.BaseDirectory}/CacheStore/{cacheNamespace}.json`。
容器（需挂卷）、systemd 只读 root、安装在 `/usr/lib` 等不可写目录的场景，请显式指定可写根目录：

```csharp
// 默认：AppContext.BaseDirectory
var store = new CacheStore("bot-state");

// 指定存储根目录 → 文件落在 {root}/CacheStore/{ns}.json
var store = new CacheStore("bot-state", storageDirectory: "/var/lib/lunarium");
```

子目录名与文件命名规则只在库内定义一处（`CacheStore.DataDirectoryName`，internal），
测试通过向构造函数注入临时目录保证隔离，**不要在测试里复刻路径约定**。

## 常用命令

```bash
# 在仓库根目录执行（根目录的 .slnx 会定位到 src 下的项目）

# 还原依赖
dotnet restore

# 生成（产物位于 src/Lunarium.Common/bin/ 下）
dotnet build

# 只构建单个项目
dotnet build src/Lunarium.Common/Lunarium.Common.csproj

# 打包为 NuGet 包
dotnet pack -c Release
```

## AOT / 裁剪兼容

本库已开启 `<IsAotCompatible>true</IsAotCompatible>`，可以安全用于 **Native AOT**（`PublishAot`）
与 **trimming**（`PublishTrimmed`）应用。

已实测：以 `PublishAot=true` 发布（`dotnet publish -c Release -r linux-x64`）得到原生可执行文件并可正常运行，
`Cache` / `CacheStore` 的序列化与落盘重载、`NanoID`、`LocalSnowflake`、`DirtyDebouncer` / `CtsDebouncer` 均无异常。

### 新增需要 JSON 序列化的类型时必须同步登记

`Cache` 的 JSON 元数据由源生成器在编译期静态产出（见 `Caching/CacheJsonContext.cs`）。
**任何需要被 `JsonSerializer` 序列化 / 反序列化的类型，都必须登记到 `CacheJsonContext`：**

```csharp
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Cache))]
[JsonSerializable(typeof(新类型))]   // ← 忘登记不会编译失败，只会在 AOT / 裁剪后运行时才报错
internal partial class CacheJsonContext : JsonSerializerContext;
```

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
- 涉及落盘路径时，先读「缓存落盘位置」一节：路径约定只在库内维护一处，测试一律注入临时目录。

## 许可证

本项目基于 [Apache License 2.0](LICENSE) 授权，Copyright © 2026 Cyanflower。

在遵守许可条款的前提下，可用于商业与非商业项目；再分发时请保留 `LICENSE` 与本文件的版权声明。
