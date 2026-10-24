# Debounce API

命名空间：`Lunarium.Common.Debounce`

两种防抖模型：

- `DirtyDebouncer`：固定周期轮询脏标记，适合“攒一批修改后定期落盘”。
- `CtsDebouncer`：取消令牌尾沿防抖，适合“最后一次调用后延迟执行一次”。

## Debouncer 工厂

```csharp
public static class Debouncer
{
    // DirtyDebouncer
    public static DirtyDebouncer CreateDirty(
        Func<CancellationToken, Task> action, TimeSpan interval);

    public static DirtyDebouncer CreateDirty(
        Func<CancellationToken, Task> action, TimeSpan interval, DirtyDebouncer.Options options);

    public static DirtyDebouncer CreateDirty(
        Action action, TimeSpan interval);

    public static DirtyDebouncer CreateDirty(
        Action action, TimeSpan interval, DirtyDebouncer.Options options);

    // CtsDebouncer
    public static CtsDebouncer CreateCts(
        Func<CancellationToken, Task> action, TimeSpan delay);

    public static CtsDebouncer CreateCts(
        Func<CancellationToken, Task> action, TimeSpan delay, CtsDebouncer.Options options);

    public static CtsDebouncer CreateCts(
        Action action, TimeSpan delay);

    public static CtsDebouncer CreateCts(
        Action action, TimeSpan delay, CtsDebouncer.Options options);
}
```

- `CreateDirty` 立即构造并启动轮询循环；`CreateCts` 只构造，首次 `Invoke()` 才开始计时。
- 同步 `Action` 重载会被包装成返回 `Task.CompletedTask` 的异步委托，不接收取消令牌。
- `action` / `options` 为 `null` → `ArgumentNullException`；`interval` / `delay` 不为正 → `ArgumentOutOfRangeException`。
- 也可以直接使用 `DirtyDebouncer` / `CtsDebouncer` 的公开构造函数；工厂只是提供了默认 `Options` 的便捷重载。

## DirtyDebouncer

```csharp
public sealed class DirtyDebouncer : IDisposable, IAsyncDisposable
{
    public DirtyDebouncer(
        Func<CancellationToken, Task> action,
        TimeSpan interval,
        Options options);

    public bool IsDirty { get; }

    public void MarkDirty();
    public Task ExecuteNowAsync();

    public void Dispose();
    public ValueTask DisposeAsync();
}
```

### Options

```csharp
public record Options
{
    public CancellationToken DebouncerCancellationToken { get; init; } = default;
    public CancellationToken ActionCancellationToken { get; init; } = default;
    public Action<Exception>? OnError { get; init; }
    public bool SafeOnError { get; init; } = true;
}
```

- `DebouncerCancellationToken`：防抖器生命周期令牌。取消后结束轮询，并在退出前把残留脏数据最后提交一次。
- `ActionCancellationToken`：调用方持有的外部令牌，原样传给 action；防抖器不会取消它。
- `OnError`：action 异常回调。
- `SafeOnError`：默认 `true`，吞掉 `OnError` 自身异常；设为 `false` 时回调异常可能透传。

### 行为

- 构造即启动；每 `interval` 检查一次 `IsDirty`，有脏数据才执行 action。
- 执行前先清除脏标记；失败则重新置脏，下一轮重试。
- action 串行执行；`ExecuteNowAsync()` 与轮询 action 通过信号量排队，不会并发。
- `ExecuteNowAsync()` 立即执行一次；成功清除脏标记，失败恢复脏标记。
- 取消 `DebouncerCancellationToken` 或调用 `DisposeAsync()` 时，若有脏数据会最后提交一次。
- 同步 `Dispose()` 只发出取消信号，不等待最后提交；需要确保收尾完成请用 `DisposeAsync()`。
- `MarkDirty()` / `ExecuteNowAsync()` 在释放后抛 `ObjectDisposedException`。
- action 抛出的 `OperationCanceledException` 视为正常取消，不报告为错误、不重试；其他异常交给 `OnError`。

## CtsDebouncer

```csharp
public sealed class CtsDebouncer : IDisposable, IAsyncDisposable
{
    public CtsDebouncer(
        Func<CancellationToken, Task> action,
        TimeSpan delay,
        Options options);

    public void Invoke();
    public Task ExecuteNowAsync();

    public void Dispose();
    public ValueTask DisposeAsync();
}
```

### Options

```csharp
public record Options
{
    public CancellationToken ActionCancellationToken { get; init; } = default;
    public TimeSpan? MaxWait { get; init; }
    public Action<Exception>? OnError { get; init; }
    public bool SafeOnError { get; init; } = true;
}
```

- `ActionCancellationToken`：外部生命周期令牌，原样传给 action；防抖器不会取消它。
- `MaxWait`：最大等待上限，默认 `null`（纯尾沿防抖）。从一次 burst 的第一次 `Invoke()` 起计时，期间后续调用不会重置；到点强制执行一次，避免持续调用导致饥饿。
- `OnError` / `SafeOnError`：同 `DirtyDebouncer`。

### 行为

- `Invoke()` 取消尚未触发的上一次延迟，并在“最后一次调用 + `delay`”后执行一次 action。
- 每次 `Invoke()` 都会重置尾沿延迟；`MaxWait` 不会因后续调用而延后。
- action 串行执行；`ExecuteNowAsync()` 与定时触发的 action 排队互斥。
- `ExecuteNowAsync()` 立即排队执行一次，**不会**取消/重置当前待触发的延迟。
- `Dispose()` 取消尚未触发的延迟，但不等待已在执行中的 action；`DisposeAsync()` 会等待所有已入队 action 完成。
- `Invoke()` / `ExecuteNowAsync()` 在释放后抛 `ObjectDisposedException`。
- action 抛出的 `OperationCanceledException` 视为正常取消；其他异常交给 `OnError`。

## 对比

| | `DirtyDebouncer` | `CtsDebouncer` |
| --- | --- | --- |
| 触发 | `MarkDirty()` | `Invoke()` |
| 执行时机 | 下一个 `interval` 轮询点 | 最后一次调用 + `delay` |
| 合并 | 脏标记 | 取消上一次延迟 |
| 最长等待 | 固定轮询 | `MaxWait` 可选 |
| 失败重试 | 自动重新置脏 | 不自动重试 |
| 关闭收尾 | 最后提交脏数据 | 等待已入队 action |
| 适合 | 周期落盘、批量脏状态 | 搜索、窗口变化、频繁配置变更 |

## 异常速查

| 场景 | 异常 |
| --- | --- |
| 构造函数 `action` / `options` 为 null | `ArgumentNullException` |
| `DirtyDebouncer` 的 `interval <= 0` | `ArgumentOutOfRangeException` |
| `CtsDebouncer` 的 `delay <= 0` 或 `MaxWait <= 0` | `ArgumentOutOfRangeException` |
| 释放后调用 `MarkDirty` / `Invoke` / `ExecuteNowAsync` | `ObjectDisposedException` |
| action 异常 | 交给 `OnError`；`SafeOnError = true` 时回调异常被吞掉 |
| `DisposeAsync()` | 默认不抛 action 异常；`SafeOnError = false` 且回调自身抛异常时可能透传 |

## 示例

尾沿防抖：

```csharp
using Lunarium.Common.Debounce;

await using var debouncer = Debouncer.CreateCts(
    async ct => await SaveAsync(ct), // SaveAsync 是你的保存逻辑
    TimeSpan.FromMilliseconds(500),
    new CtsDebouncer.Options { MaxWait = TimeSpan.FromSeconds(5) });

debouncer.Invoke();
```

脏标记防抖：

```csharp
await using var saver = Debouncer.CreateDirty(
    async ct => await SaveAsync(ct),
    TimeSpan.FromSeconds(5));

saver.MarkDirty();
```

## 线程安全 / AOT

- `MarkDirty` / `Invoke` 可并发调用；action 始终串行。
- 释放与正在执行的 action 的并发由调用方负责协调：不要在释放的同时继续投递新工作。
- 全部基于 `Task` / `CancellationToken` / 定时器，无反射、无动态代码，AOT 安全。
- 更完整的选型与落盘约定见[使用指南](../README.md)。

可运行示例：[DebounceExample.cs](../examples/DebounceExample.cs)。
