using System.Collections.Concurrent;
using Lunarium.Common.Debounce;

namespace Lunarium.Common.Caching;

/// <summary>
/// 提供高性能内存并发访问与防抖异步磁盘持久化能力的强类型标量 KV 缓存存储器。适用于 KB 级或 &lt;= 8 MB 的轻量级缓存数据存储。
/// </summary>
public sealed class CacheStore : IAsyncDisposable
{
    /// <summary>
    /// 默认的落盘子目录名（位于存储根目录之下）。
    /// </summary>
    internal const string DataDirectoryName = "CacheStore";

    // 缓存数据
    private readonly Cache _cache;
    private readonly DirtyDebouncer _debouncer;
    private readonly CancellationTokenSource _cts = new();
    private readonly JsonFileHandler _jsonFileHandler;
    private readonly string _cacheNamespace;
    private bool _isDisposed;
    private Action<Exception>? _logError;

    private static readonly ConcurrentDictionary<string, byte> CacheStorePaths = new();

    /// <summary>
    /// 初始化 <see cref="CacheStore"/> 的新实例。
    /// 从本地磁盘加载历史缓存，并配置 5 秒延迟的防抖持久化任务。
    /// </summary>
    /// <param name="cacheNamespace">缓存命名空间，用于区分不同的缓存存储器实例，避免文件路径冲突。</param>
    /// <param name="logError">可选的异常日志记录回调，用于捕获发生的异常。</param>
    /// <param name="allowCorruptRebuild">是否允许在加载缓存文件时遇到损坏或无法解析的情况时，自动重建一个空缓存并继续运行。默认为 <c>false</c>，即遇到损坏文件时会抛出异常。</param>
    /// <param name="storageDirectory">
    /// 存储根目录，缓存文件落在其下的 <c>CacheStore</c> 子目录中，文件名为 <c>{cacheNamespace}.json</c>。
    /// 默认为 <c>null</c>，即使用 <see cref="AppContext.BaseDirectory"/>；
    /// 容器（需挂卷）、systemd 只读 root、安装在 <c>/usr/lib</c> 等场景应显式指定可写目录。
    /// 传入相对路径时会被解析为基于进程当前目录的绝对路径。
    /// </param>
    /// <remarks>
    /// 落盘位置：<c>{storageDirectory ?? AppContext.BaseDirectory}/CacheStore/{cacheNamespace}.json</c>。
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="cacheNamespace"/> 为空或含非法字符、<paramref name="storageDirectory"/> 为空白字符串，或 <paramref name="allowCorruptRebuild"/> 为 <c>true</c> 却未提供 <paramref name="logError"/> 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当指定的缓存命名空间已被其他实例使用时抛出。</exception>
    /// <exception cref="CacheFileCorruptException">当缓存文件存在但内容损坏，且 <paramref name="allowCorruptRebuild"/> 为 <c>false</c> 时抛出。</exception>
    public CacheStore(string cacheNamespace, Action<Exception>? logError = null, bool allowCorruptRebuild = false, string? storageDirectory = null)
    {
        // 校验参数合法性
        ArgumentException.ThrowIfNullOrEmpty(cacheNamespace, nameof(cacheNamespace));
        if (storageDirectory is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory, nameof(storageDirectory));
        if (allowCorruptRebuild && logError is null)
            throw new ArgumentNullException(nameof(logError), "allowCorruptRebuild 需要同时提供 logError, 否则损坏文件会被静默丢弃。");
        if (Path.GetInvalidFileNameChars().Any(c => cacheNamespace.Contains(c)))
            throw new ArgumentException("cacheNamespace 包含路径非法字符", nameof(cacheNamespace));
        if (!CacheStorePaths.TryAdd(cacheNamespace, 0))
            throw new InvalidOperationException($"缓存命名空间重复: {cacheNamespace}");

        // TryAdd 成功之后, 任何失败都必须释放命名空间占位, 允许调用方修正后重试
        try
        {
            _cacheNamespace = cacheNamespace;
            _logError = logError;

            // 相对路径按进程当前目录解析为绝对路径, 避免运行期 CWD 变化导致落盘位置漂移
            string storageRoot = storageDirectory is null
                ? AppContext.BaseDirectory
                : Path.GetFullPath(storageDirectory);

            _jsonFileHandler = new JsonFileHandler(Path.Combine(storageRoot, DataDirectoryName, $"{cacheNamespace}.json"));

            try
            {
                _cache = _jsonFileHandler.LoadFromFile();
            }
            catch (CacheFileCorruptException ex) when (allowCorruptRebuild)
            {
                // 允许重建: 记录异常 + 把损坏文件改名留档(尽力而为) + 以空缓存继续
                OnError(ex);
                try { _jsonFileHandler.ArchiveCorruptFile(); }
                catch (Exception archiveEx) { OnError(archiveEx); }
                _cache = new Cache();
            }

            var dirtyDebouncerOptions = new DirtyDebouncer.Options
            {
                ActionCancellationToken = _cts.Token,
                SafeOnError = true,
                OnError = OnError
            };
            _debouncer = Debouncer.CreateDirty(SaveFile, TimeSpan.FromSeconds(5), dirtyDebouncerOptions);
        }
        catch
        {
            CacheStorePaths.TryRemove(cacheNamespace, out _);
            throw;
        }
    }

    /// <summary>
    /// 设置或更新指定键的缓存值，并标记数据已更改以触发防抖落盘。
    /// </summary>
    /// <typeparam name="T">
    /// 缓存值的类型。支持基础基元类型：
    /// <see cref="string"/>、<see cref="int"/>、<see cref="long"/>、<see cref="short"/>、
    /// <see cref="uint"/>、<see cref="ulong"/>、<see cref="double"/>、<see cref="float"/>、<see cref="bool"/>。
    /// </typeparam>
    /// <param name="key">缓存的键名。</param>
    /// <param name="value">要存入的缓存数据。</param>
    /// <exception cref="ObjectDisposedException">当缓存对象已被释放后调用时抛出。</exception>
    /// <exception cref="NotSupportedException">当传入的类型不受底层强类型桶支持时抛出。</exception>
    public void Set<T>(string key, T value)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _cache.Set(key, value);
        _debouncer.MarkDirty();
    }

    /// <summary>
    /// 从内存中获取指定键的缓存值。
    /// </summary>
    /// <typeparam name="T">期望读取的目标类型。</typeparam>
    /// <param name="key">缓存的键名。</param>
    /// <param name="defaultValue">当键不存在或类型不匹配时返回的默认回退值（默认为该类型的 default）。</param>
    /// <returns>读取到的强类型缓存值；若未命中则返回 <paramref name="defaultValue"/>。</returns>
    /// <exception cref="ObjectDisposedException">当缓存对象已被释放后调用时抛出。</exception>
    public T? Get<T>(string key, T? defaultValue = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return _cache.Get(key, defaultValue);

    }

    /// <summary>
    /// 从所有缓存桶中删除指定键的数据。
    /// </summary>
    /// <param name="key">要删除的键名。</param>
    /// <returns>若成功找到并移除了该键则返回 <see langword="true"/>，并触发防抖落盘；若键本就不存在则返回 <see langword="false"/>。</returns>
    /// <exception cref="ObjectDisposedException">当缓存对象已被释放后调用时抛出。</exception>
    public bool Remove(string key)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var success = _cache.Remove(key);
        if (success)
        {
            _debouncer.MarkDirty();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 立即强制刷盘，不等待防抖倒计时
    /// </summary>
    public Task FlushAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return _debouncer.ExecuteNowAsync();
    }

    /// <summary>
    /// 异步将内存中的缓存数据全量序列化并写入到本地 JSON 文件中。
    /// </summary>
    /// <param name="ct">用于取消写入操作的取消令牌。</param>
    private async Task SaveFile(CancellationToken ct)
    {
        await _jsonFileHandler.SaveToFileAsync(_cache, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步释放缓存存储器占用的资源。
    /// 会优先等待并确保当前未落盘的脏数据立即写入磁盘，随后释放定时器与取消令牌。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            await _debouncer.ExecuteNowAsync().ConfigureAwait(false);
        }
        finally
        {
            _cts.Cancel();
            await _debouncer.DisposeAsync().ConfigureAwait(false);
            _cts.Dispose();
            CacheStorePaths.TryRemove(_cacheNamespace, out _);
        }
    }

    private void OnError(Exception ex)
    {
        _logError?.Invoke(ex);
    }
}