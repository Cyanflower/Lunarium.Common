# 可运行示例

`docs/examples/` 下的控制台项目，按模块演示 `Lunarium.Common` 的典型用法。示例会使用独立的环境变量名和临时目录，运行后自动清理，不依赖外部服务或网络。

## 运行

在仓库根目录执行：

```bash
# 全部示例
dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- all

# 单个场景
dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- env
dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- ids
dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- debounce
dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- cache
dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- document
```

## 场景

| 命令 | 覆盖内容 | 源文件 |
| --- | --- | --- |
| `env` | `EnvVar`、`ProxyUrl`、`EnvUrl` | `EnvExample.cs` |
| `ids` | `NanoId`、`LocalSnowflake` | `IdExample.cs` |
| `debounce` | `CtsDebouncer`、`DirtyDebouncer` | `DebounceExample.cs` |
| `cache` | `CacheStore` 的写入、刷盘、重载 | `CacheExample.cs` |
| `document` | `JsonDocumentStore<T>` 的快照、源生成、重载 | `DocumentExample.cs` |
| `all` | 依次运行以上全部 | `Program.cs` |

## 说明

- 示例项目已加入 `Lunarium.Common.slnx`；CI 会编译并运行 `dotnet run ... -- all`。
- 示例项目面向 `net10.0`；类库本身支持 `net8.0` / `net9.0` / `net10.0`。
- 示例使用 `Path.GetTempPath()` 下的随机目录；正常结束后会删除。
- 示例只用于演示 API 用法，不构成生产配置建议；容器、systemd 等场景请显式指定可写目录。
- 更完整的使用指南见 [docs/README.md](../README.md)，API 速查见 [docs/api/](../api/README.md)。
