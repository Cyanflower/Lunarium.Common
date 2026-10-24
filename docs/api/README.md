# API 速查

> 公共 API 的签名与行为速查。完整的使用场景、选型与边界说明见[使用指南](../README.md)。
>
> 目标版本：1.4.x ｜ 目标框架：`net8.0` / `net9.0` / `net10.0`

## 页面索引

| 模块 | 命名空间 | API 页面 | 主要类型 |
| --- | --- | --- | --- |
| `Alphabet` | `Lunarium.Common` | [alphabet.md](alphabet.md) | `Alphabet` |
| `EnvVar` | `Lunarium.Common` | [env-var.md](env-var.md) | `EnvVar` |
| `Net` | `Lunarium.Common.Net` | [net.md](net.md) | `ProxyUrl`、`EnvUrl` |
| `UID` | `Lunarium.Common.UID` | [uid.md](uid.md) | `NanoId`、`LocalSnowflake` |
| `Debounce` | `Lunarium.Common.Debounce` | [debounce.md](debounce.md) | `Debouncer`、`DirtyDebouncer`、`CtsDebouncer` |
| `Caching` | `Lunarium.Common.Caching` | [caching.md](caching.md) | `Cache`、`CacheStore`、`JsonDocumentStore<T>`、`CacheFileCorruptException` |

## 阅读约定

- 签名只列公开成员；可选参数按声明顺序写出默认值。
- `T?` / `string?` 表示可空引用或可空值；具体以源码的可空注解为准。
- “异常”只列该成员主动文档化的异常；IO、平台拒绝等底层异常仍可能透传。
- “线程安全”描述并发调用是否安全，不代表可以并发 `DisposeAsync`。
- 本目录只覆盖 public API；`CacheJsonContext`、`CacheFileRegistry`、`JsonFileHandler` 等 internal 类型不在 API 范围内。
- AOT / trimming：库已开启 `IsAotCompatible`；JSON 路径使用源生成，不依赖反射序列化。具体约束见[使用指南](../README.md)的 [AOT 一节](../README.md#aot)。

## 公共类型总览

| 类型 | 种类 | 接口 / 约束 | 一句话 |
| --- | --- | --- | --- |
| `Alphabet` | `static class` | — | 常用字符集常量 |
| `EnvVar` | `sealed record` | — | 环境变量包装与类型化解析 |
| `ProxyUrl` | `sealed record` | — | 代理 URL + 环境变量读取 |
| `EnvUrl` | `sealed record` | — | 普通环境 URL |
| `NanoId` | `static class` | — | 无偏随机短 ID |
| `LocalSnowflake` | `static class` | — | 单机雪花 ID |
| `Debouncer` | `static class` | — | 防抖器工厂 |
| `DirtyDebouncer` | `sealed class` | `IDisposable`、`IAsyncDisposable` | 轮询脏标记防抖 |
| `CtsDebouncer` | `sealed class` | `IDisposable`、`IAsyncDisposable` | 取消令牌尾沿防抖 |
| `Cache` | `class` | — | 标量 KV 底层容器 |
| `CacheStore` | `sealed class` | `IAsyncDisposable` | 内存 KV + 防抖落盘 |
| `JsonDocumentStore<T>` | `sealed class` | `IAsyncDisposable`，`T : class, new()` | 单文档快照 + 防抖落盘 |
| `CacheFileCorruptException` | `sealed class` | 继承 `InvalidOperationException` | 缓存/文档文件损坏 |

## 通用释放约定

- `CacheStore`、`JsonDocumentStore<T>` 只实现 `IAsyncDisposable`；使用 `await using`。
- `DirtyDebouncer`、`CtsDebouncer` 同时实现 `IDisposable` / `IAsyncDisposable`；优先 `await using`。
- 释放后调用公开读写/刷盘方法会抛 `ObjectDisposedException`。
- `FlushAsync()` 立即刷盘；`DisposeAsync()` 负责最终收尾；同步 `Dispose()` 只适用于防抖器，且不等待最终落盘。
- 详见[使用指南的“释放与刷盘”](../README.md#conventions)。
