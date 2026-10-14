namespace Lunarium.Common.Debounce;

/// <summary>
/// 基于取消令牌（CTS）的尾沿防抖器：连续调用 <see cref="Invoke"/> 只会触发一次 action，
/// 且发生在「最后一次调用 + <c>delay</c>」之后；可用 <see cref="Options.MaxWait"/> 限制最长等待，避免持续调用导致饥饿。
/// </summary>
/// <remarks>
/// action 串行执行（同一时刻最多一个），且与 <see cref="ExecuteNowAsync"/> 排队互斥；
/// action 收到的取消令牌始终是调用方提供的外部令牌，本类型<b>不会</b>取消它。
/// </remarks>
public sealed class CtsDebouncer : IDisposable, IAsyncDisposable
{
    // action 收到的 CancellationToken 为「外部令牌」(由 options.ActionCancellationToken 提供),
    // 其取消只由调用方管理; 防抖器自身的 Dispose 永不取消它.
    private readonly Func<CancellationToken, Task> _action;
    private readonly TimeSpan _delay;
    private readonly TimeSpan? _maxWait;
    private readonly CancellationToken _externalToken;
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    // System.Threading.Lock 自 .NET 9 引入; 低版本退回 object(由 Monitor 提供等价互斥语义)
    private readonly object _lock = new();
#endif

    // 内部防抖 CTS: 链接外部令牌(单向), 仅用于 Task.Delay.
    private CancellationTokenSource? _cts;
    private readonly Action<Exception>? _onError;
    private readonly bool _safeOnError;
    private bool _disposed;

    // 串行链尾: 所有 action 执行按入队顺序串成一条链(前一环 await 完才跑下一环),
    // 保证同一时刻最多一个 action 在执行; DisposeAsync 等链尾即可覆盖全部已入队的 action.
    private Task _actionTail = Task.CompletedTask;

    // 当前 burst 的最大等待截止时刻; null 表示「无活动 burst」或「未启用 MaxWait」.
    // 一个 burst 从第一拍 Invoke 开始计时, 期间后续 Invoke 不会重置它.
    private DateTimeOffset? _burstDeadline;

    /// <summary>
    /// 基于取消令牌(CTS)的防抖器配置选项
    /// </summary>
    public record Options
    {
        /// <summary>
        /// 外部生命周期取消令牌(默认为 <see cref="CancellationToken.None"/>).<br/>
        /// 该令牌由<b>调用方</b>持有与管理: 防抖器在调用 action 时原样透传给它,
        /// 并且防抖器的 <see cref="Dispose"/>/<see cref="DisposeAsync"/> <b>不会</b>取消它.
        /// </summary>
        public CancellationToken ActionCancellationToken { get; init; } = default;

        /// <summary>
        /// 最大等待上限(默认为 <c>null</c>, 即不限制).<br/>
        /// 从一次 burst(连续调用的第一拍)开始计时: 即使期间调用不断导致尾沿延迟被反复重置,
        /// 到达此上限时也会强制执行一次 action, 避免饥饿. <c>null</c> = 纯尾沿防抖.
        /// </summary>
        public TimeSpan? MaxWait { get; init; }

        /// <summary>
        /// 发生异常时的错误处理回调
        /// </summary>
        public Action<Exception>? OnError { get; init; }

        /// <summary>
        /// 是否安全调用 <see cref="OnError"/> 回调.<br/>
        /// <c>true</c>: 防御性吞掉回调内部异常(不可观测), 防止击穿线程池;<br/>
        /// <c>false</c>: 允许抛出回调内部异常(可观测). 默认为 <c>true</c>.
        /// </summary>
        public bool SafeOnError { get; init; } = true;
    }

    /// <summary>
    /// 初始化 <see cref="CtsDebouncer"/> 的新实例；构造后即可调用 <see cref="Invoke"/>。
    /// </summary>
    /// <param name="action">待防抖执行的异步操作，接收调用方提供的外部取消令牌。</param>
    /// <param name="delay">尾沿防抖延迟，必须大于 <see cref="TimeSpan.Zero"/>。</param>
    /// <param name="options">配置项，见 <see cref="Options"/>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> 或 <paramref name="options"/> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> 或 <see cref="Options.MaxWait"/> 不为正数。</exception>
    public CtsDebouncer(Func<CancellationToken, Task> action, TimeSpan delay, Options options)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);
        // 必须为正: Task.Delay 不接受非正延迟; 正值也保证 RunAsync 会先 await 让出,
        // 从而不会在 Invoke 持锁期间执行用户回调(见 Invoke 中的说明).
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);
        if (options.MaxWait is { } maxWait)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxWait, TimeSpan.Zero);

        _action = action;
        _delay = delay;
        _maxWait = options.MaxWait;
        _externalToken = options.ActionCancellationToken;
        _onError = options.OnError;
        _safeOnError = options.SafeOnError;
    }

    /// <summary>
    /// 触发一次（防抖的）调用：取消尚未触发的上一次延迟，并在 <c>delay</c> 之后执行一次 action。
    /// </summary>
    /// <exception cref="ObjectDisposedException">实例已被 <see cref="Dispose"/> / <see cref="DisposeAsync"/> 释放。</exception>
    public void Invoke()
    {
        lock (_lock)
        {
            // 检查放在锁内, 避免与并发的 Dispose/DisposeAsync 产生「已释放仍新建」的竞态
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 一个 burst 的第一拍开启 maxWait 计时; 后续 Invoke 不重置它
            if (_maxWait is { } maxWait && _burstDeadline is null)
                _burstDeadline = DateTimeOffset.UtcNow + maxWait;

            _cts?.Cancel();
            _cts?.Dispose();
            // 链接外部令牌: 外部取消时未触发的延迟随之取消(单向, 不会反向取消外部源)
            var cts = _cts = CancellationTokenSource.CreateLinkedTokenSource(_externalToken);

            // 在锁内启动: RunAsync 会先 await 让出, 因此持锁期间不会执行用户回调
            // (依赖构造函数已校验 delay > 0), 也避免与 DisposeAsync 并发读取已释放的 cts.Token.
            // 无需登记该任务: 完成延迟的任务会在锁内把自己挂进串行链, DisposeAsync 等链尾即可.
            _ = RunAsync(cts);
        }
    }

    private async Task RunAsync(CancellationTokenSource cts)
    {
        var ct = cts.Token;

        // 有效等待 = min(防抖延迟, maxWait 剩余); maxWait 已到点则立即执行
        var wait = _delay;
        if (_burstDeadline is { } deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining < wait) wait = remaining;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        }

        // 等待阶段: 仅在此处吞掉防抖取消异常
        try
        {
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct).ConfigureAwait(false);
            else
                await Task.Yield(); // 已到点: 让出以避免在 Invoke 持锁期间执行 action
        }
        catch (OperationCanceledException)
        {
            // 防抖生效(被后续 Invoke 或外部令牌取消), 直接退出即可
            return;
        }

        // 抢占 burst 所有权 + 入队(同一临界区): 若已被后续 Invoke 取代则放弃本次
        Task toAwait;
        lock (_lock)
        {
            if (!ReferenceEquals(_cts, cts)) return;
            _cts = null;
            _burstDeadline = null; // 结束 burst
            cts.Dispose();
            // 在同一把锁内入队, 保证「会跑 action 的任务」在 DisposeAsync 抓锁快照链尾前已被挂上
            toAwait = EnqueueActionLocked();
        }

        await toAwait.ConfigureAwait(false);
    }

    /// <summary>
    /// 立即执行一次 action, 不受防抖延迟影响, 也不会取消/重置当前待触发的延迟.<br/>
    /// 适用场景:
    /// <list type="bullet">
    ///   <item>优雅关闭前主动落盘/提交一次最终快照;</item>
    ///   <item>按调用次数强制刷新(例如每 N 次 <see cref="Invoke"/> 就执行一次).</item>
    /// </list>
    /// <b>注意</b>: 与防抖触发的 action <b>串行执行</b>(排队等待, 不并发); 
    /// 且须在 <see cref="Dispose"/>/<see cref="DisposeAsync"/> 之前调用.
    /// </summary>
    public async Task ExecuteNowAsync()
    {
        Task toAwait;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            toAwait = EnqueueActionLocked();
        }

        await toAwait.ConfigureAwait(false);
    }

    // 串行链尾入队(必须在 _lock 内调用): 把一次 action 挂到链尾, 保证任一时刻只有一个 action 在执行
    private Task EnqueueActionLocked()
    {
        var previous = _actionTail;
        // 截断链路: async 状态机会把 previous 提升成字段且完成后不清理(实测 .NET 10),
        // 不截断则 _actionTail 会一直牵引整条历史 Task 链 → 内存泄漏. 空闲时换成单例即可断链.
        if (previous.IsCompleted)
            previous = Task.CompletedTask;
        return _actionTail = ChainActionAsync(previous);
    }

    // 等前一个 action 跑完再跑本次
    private async Task ChainActionAsync(Task previous)
    {
        // 前驱无论成功/取消/故障都不应阻断本次排队; 吞掉前驱异常(顺带标记为已观测), 避免毒化链路
        try { await previous.ConfigureAwait(false); }
        catch (Exception) { /* 忽略前驱异常 */ }

        // previous 通常已完成 → 若直接往下会仍在 EnqueueActionLocked 的持锁栈上执行用户回调;
        // 强制让出一次, 确保 action 在锁外、且在调用方释放锁之后才真正开始
        await Task.Yield();
        await RunActionAsync().ConfigureAwait(false);
    }

    // 执行阶段: 与延迟分离, 避免 action 抛的 OperationCanceledException 被防抖生效的 catch 吞
    private async Task RunActionAsync()
    {
        try
        {
            // 透传外部令牌: 仅由调用方决定是否取消, 防抖器的 Dispose 不会取消它
            await _action(_externalToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常取消退出, 不需要往外抛异常
        }
        catch (Exception ex)
        {
            _onError.SafeInvoke(ex, _safeOnError);
        }
    }

    /// <summary>
    /// 同步释放: 取消尚未触发的延迟, 但<b>不会</b>等待已在执行中的 action 完成.<br/>
    /// 需要优雅收尾(等待在途 action 执行完毕)请改用 <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            // 只取消内部 CTS(停止未触发的延迟), 绝不触碰外部令牌
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 异步释放: 阻止后续 <see cref="Invoke"/>、取消尚未触发的延迟, 
    /// 并等待所有已入队的 action 执行完毕后再返回.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Task tail;
        lock (_lock)
        {
            if (!_disposed)
            {
                _disposed = true;
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            tail = _actionTail; // 快照串行链尾
        }

        // RunActionAsync 内部已吞掉全部异常(含 OperationCanceledException), 此处 await 不会抛出
        await tail.ConfigureAwait(false);
    }
}