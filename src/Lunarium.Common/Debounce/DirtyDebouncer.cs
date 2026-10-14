namespace Lunarium.Common.Debounce;

/// <summary>
/// 脏状态防抖器：按固定周期轮询「脏」标记，有脏数据时执行一次 action 并清除标记；
/// 执行失败则重新置脏，等待下一轮重试。
/// </summary>
/// <remarks>
/// 构造即启动轮询循环；取消生命周期令牌（<see cref="Options.DebouncerCancellationToken"/>）或调用
/// <see cref="DisposeAsync"/> 会结束循环，并在退出前把残留的脏数据最后提交一次。
/// </remarks>
public sealed class DirtyDebouncer : IDisposable, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _action;
    private readonly TimeSpan _interval;
    private volatile bool _isDirty;

    // 外部令牌: 由调用方持有, 原样透传给 action; 防抖器不会取消它
    private readonly CancellationToken _actionToken;

    // 内部生命周期 CTS: 链接外部 DebouncerCancellationToken(单向).
    // 取消它(或外部令牌) → 结束轮询循环, 并在退出前对未落盘的脏数据做最后一次提交.
    private readonly CancellationTokenSource _cts;

    private readonly Task _loopTask;
    private readonly Action<Exception>? _onError;
    private readonly bool _safeOnError;
    private int _disposed;

    // 串行闸门: 保证循环内的落盘与 ExecuteNowAsync 不会并发(同一时刻最多一个 action)
    private readonly SemaphoreSlim _actionGate = new(1, 1);

    /// <summary>
    /// 脏状态防抖器的配置选项
    /// </summary>
    public record Options
    {
        /// <summary>
        /// 防抖器生命周期取消令牌（默认为 <see cref="CancellationToken.None"/>）。<br/>
        /// 取消它会结束轮询循环，并在退出前对未落盘的脏数据做最后一次提交。
        /// </summary>
        public CancellationToken DebouncerCancellationToken { get; init; } = default;

        /// <summary>
        /// 外部生命周期取消令牌（默认为 <see cref="CancellationToken.None"/>）。<br/>
        /// 该令牌由<b>调用方</b>持有与管理：防抖器在调用 action 时原样透传给它，
        /// 并且防抖器自身（含由 <see cref="DebouncerCancellationToken"/> 触发的收尾）不会取消它。
        /// </summary>
        public CancellationToken ActionCancellationToken { get; init; } = default;

        /// <summary>
        /// 发生异常时的错误处理回调
        /// </summary>
        public Action<Exception>? OnError { get; init; }

        /// <summary>
        /// 是否安全调用 <see cref="OnError"/> 回调<br/>
        /// <c>true</c>：防御性吞掉回调内部异常（不可观测），防止击穿线程池；<br/>
        /// <c>false</c>：允许抛出回调内部异常（可观测）。默认为 <c>true</c>。
        /// </summary>
        public bool SafeOnError { get; init; } = true;
    }

    /// <summary>
    /// 初始化 <see cref="DirtyDebouncer"/> 的新实例，并立即启动轮询循环。
    /// </summary>
    /// <param name="action">待防抖执行的异步操作，接收调用方提供的外部取消令牌。</param>
    /// <param name="interval">检查并合并脏数据的轮询周期，必须大于 <see cref="TimeSpan.Zero"/>。</param>
    /// <param name="options">配置项，见 <see cref="Options"/>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> 或 <paramref name="options"/> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> 不为正数。</exception>
    public DirtyDebouncer(Func<CancellationToken, Task> action, TimeSpan interval, Options options)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);
        // 必须为正: PeriodicTimer 不接受非正周期, 提前挡住避免循环任务才异步抛错
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        _action = action;
        _interval = interval;
        _actionToken = options.ActionCancellationToken;
        _onError = options.OnError;
        _safeOnError = options.SafeOnError;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(options.DebouncerCancellationToken);

        // 构造即启动轮询循环(取消 _cts 或外部令牌即退出)
        _loopTask = DirtyFlushAsync(_cts.Token);
    }

    private async Task DirtyFlushAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (!_isDirty) continue;
                _isDirty = false;
                // 写失败则重新置脏, 下次重试(正常取消不视为失败)
                if (!await RunActionAsync().ConfigureAwait(false))
                    _isDirty = true;
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消退出, 不需要往外抛异常
            // 如果取消时有脏数据, 结束前最后落盘一次
            if (_isDirty)
                await RunActionAsync().ConfigureAwait(false);
        }
    }

    // 串行执行一次 action; 返回 true = 无需重试(成功或被正常取消), false = 执行失败
    private async Task<bool> RunActionAsync()
    {
        await _actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _action(_actionToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            // 外部令牌取消导致的正常中止: 不当作写入失败, 不重试、不误报
            return true;
        }
        catch (Exception ex)
        {
            _onError.SafeInvoke(ex, _safeOnError);
            return false;
        }
        finally
        {
            _actionGate.Release();
        }
    }

    /// <summary>
    /// 当前是否有尚未落盘的脏数据(供排查/UI 状态绑定).
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// 标记有脏数据待落盘. 已释放后调用会抛出 <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void MarkDirty()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        _isDirty = true;
    }

    /// <summary>
    /// 立即执行一次 action(落盘), 不受轮询周期影响.<br/>
    /// 与循环内的落盘通过信号量<b>串行</b>(排队等待, 不并发); 成功则清除脏标记, 失败则保留脏标记待下轮重试.<br/>
    /// 须在 <see cref="Dispose"/>/<see cref="DisposeAsync"/> 之前调用.
    /// </summary>
    public async Task ExecuteNowAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        // 与循环一致: 先清脏标记再落盘; 失败则恢复脏标记(下次重试)
        _isDirty = false;
        if (!await RunActionAsync().ConfigureAwait(false))
            _isDirty = true;
    }

    /// <summary>
    /// 同步释放: 停止轮询循环, 但<b>不会</b>等待退出前的最后落盘完成.<br/>
    /// 需要确保最后一次落盘写入完成, 请改用 <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        // 只发出取消信号; CTS 的物理释放留给 DisposeAsync(在 await 循环结束后),
        // 避免循环正重新 WaitForNextTickAsync / 注册 token 时被 Dispose 引发 ObjectDisposedException
        _cts.Cancel();
    }

    /// <summary>
    /// 异步释放: 停止轮询循环, 并等待退出前的最后落盘(以及任何在途 action)完成后再返回.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _cts.Cancel();

        // 等待循环最终退出(含退出前的最后落盘)
        await _loopTask.ConfigureAwait(false);

        _cts.Dispose();
    }
}