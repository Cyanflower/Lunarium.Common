namespace Lunarium.Common.Debounce;

/// <summary>
/// 防抖器工厂，提供脏状态防抖（DirtyDebouncer）与取消令牌防抖（CtsDebouncer）的创建方法
/// </summary>
public static class Debouncer
{
    #region DirtyDebouncer 工厂方法

    /// <summary>
    /// 创建并启动一个脏状态防抖器（异步任务，使用默认配置）
    /// </summary>
    /// <param name="action">待防抖执行的异步操作（接收由 options 提供的外部取消令牌）</param>
    /// <param name="interval">检查与合并的周期时间间隔</param>
    /// <returns>已启动的脏状态防抖器实例</returns>
    public static DirtyDebouncer CreateDirty(Func<CancellationToken, Task> action, TimeSpan interval)
        => CreateDirty(action, interval, new());

    /// <summary>
    /// 创建并启动一个脏状态防抖器（异步任务）
    /// </summary>
    /// <param name="action">待防抖执行的异步操作（接收由 options 提供的外部取消令牌）</param>
    /// <param name="interval">检查与合并的周期时间间隔</param>
    /// <param name="options">防抖器配置项（含防抖器生命周期令牌、外部取消令牌、异常回调及安全策略）</param>
    /// <returns>已启动的脏状态防抖器实例</returns>
    public static DirtyDebouncer CreateDirty(Func<CancellationToken, Task> action, TimeSpan interval, DirtyDebouncer.Options options)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);
        return new DirtyDebouncer(action, interval, options);
    }

    /// <summary>
    /// 创建并启动一个脏状态防抖器（同步任务，使用默认配置）
    /// </summary>
    /// <param name="action">待防抖执行的同步操作</param>
    /// <param name="interval">检查与合并的周期时间间隔</param>
    /// <returns>已启动的脏状态防抖器实例</returns>
    public static DirtyDebouncer CreateDirty(Action action, TimeSpan interval)
        => CreateDirty(action, interval, new());

    /// <summary>
    /// 创建并启动一个脏状态防抖器（同步任务）
    /// </summary>
    /// <param name="action">待防抖执行的同步操作</param>
    /// <param name="interval">检查与合并的周期时间间隔</param>
    /// <param name="options">防抖器配置项（含防抖器生命周期令牌、外部取消令牌、异常回调及安全策略）</param>
    /// <returns>已启动的脏状态防抖器实例</returns>
    public static DirtyDebouncer CreateDirty(Action action, TimeSpan interval, DirtyDebouncer.Options options)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CreateDirty(_ => { action(); return Task.CompletedTask; }, interval, options);
    }

    #endregion

    #region CtsDebouncer 工厂方法

    /// <summary>
    /// 创建一个基于取消令牌（CTS）的防抖器（异步任务，使用默认配置）
    /// </summary>
    /// <param name="action">待防抖执行的异步操作（接收由 options 提供的外部取消令牌）</param>
    /// <param name="delay">防抖延迟等待时间（在最后一次调用后等待此时间才执行）</param>
    /// <returns>已初始化的 CTS 防抖器实例</returns>
    public static CtsDebouncer CreateCts(Func<CancellationToken, Task> action, TimeSpan delay)
        => CreateCts(action, delay, new());

    /// <summary>
    /// 创建一个基于取消令牌（CTS）的防抖器（异步任务）
    /// </summary>
    /// <param name="action">待防抖执行的异步操作（接收由 options 提供的外部取消令牌）</param>
    /// <param name="delay">防抖延迟等待时间（在最后一次调用后等待此时间才执行）</param>
    /// <param name="options">防抖器配置项（含外部取消令牌、最大等待上限、异常回调及安全策略）</param>
    /// <returns>已初始化的 CTS 防抖器实例</returns>
    public static CtsDebouncer CreateCts(Func<CancellationToken, Task> action, TimeSpan delay, CtsDebouncer.Options options)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);
        return new CtsDebouncer(action, delay, options);
    }

    /// <summary>
    /// 创建一个基于取消令牌（CTS）的防抖器（同步任务，使用默认配置）
    /// </summary>
    /// <param name="action">待防抖执行的同步操作</param>
    /// <param name="delay">防抖延迟等待时间（在最后一次调用后等待此时间才执行）</param>
    /// <returns>已初始化的 CTS 防抖器实例</returns>
    public static CtsDebouncer CreateCts(Action action, TimeSpan delay)
        => CreateCts(action, delay, new());

    /// <summary>
    /// 创建一个基于取消令牌（CTS）的防抖器（同步任务）
    /// </summary>
    /// <param name="action">待防抖执行的同步操作</param>
    /// <param name="delay">防抖延迟等待时间（在最后一次调用后等待此时间才执行）</param>
    /// <param name="options">防抖器配置项（含外部取消令牌、最大等待上限、异常回调及安全策略）</param>
    /// <returns>已初始化的 CTS 防抖器实例</returns>
    public static CtsDebouncer CreateCts(Action action, TimeSpan delay, CtsDebouncer.Options options)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CreateCts(_ => { action(); return Task.CompletedTask; }, delay, options);
    }

    #endregion
}