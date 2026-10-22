using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Lunarium.Common.Debounce;

namespace Lunarium.Common.Caching;

/// <summary>
/// 提供「单个强类型 JSON 文档」的内存快照与防抖异步磁盘持久化能力的存储。适用于配置、状态等已知模型。
/// </summary>
/// <typeparam name="T">文档根类型，需能通过调用方提供的 <see cref="JsonTypeInfo{T}"/> 序列化/反序列化。</typeparam>
/// <remarks>
/// <para>
/// 与 <see cref="CacheStore"/> 的区别：本类型持久化一个完整的文档，不提供键值访问，<b>也不做任何业务校验</b>。
/// 校验属于调用方策略（例如加载后调用 <c>Validator.TryValidateObject</c>、更新前自行检查范围），
/// 不应放在保存阶段——落盘发生在后台防抖任务中，校验失败没有调用方可见的返回通道。
/// </para>
/// <para>
/// <b>快照语义</b>：<see cref="Read"/> 返回深拷贝，<see cref="Set"/> 与 <see cref="Update"/> 同样以深拷贝入账
/// （深拷贝通过 JSON 往返完成）。调用方持有的实例与存储内部状态互不影响；代价是标记了
/// <see cref="JsonIgnoreAttribute"/> 的成员不参与快照/落盘（本存储以 JSON 中的内容为准）。
/// </para>
/// <para>
/// <b>AOT/裁剪</b>：本类型只使用 <see cref="JsonSerializer"/> 的 <see cref="JsonTypeInfo{T}"/> 重载，
/// 不会触发反射路径。调用方必须传入源生成上下文产出的类型信息，例如
/// <c>MyJsonContext.Default.MyDocument</c>（<c>JsonSerializerOptions</c> 的编码器、缩进、注释处理等也由该上下文决定）。
/// </para>
/// <para>
/// 落盘位置：<c>{storageDirectory ?? AppContext.BaseDirectory/JsonDocumentStore}/{documentName}.json</c>。
/// 文件路径注册表与 <see cref="CacheStore"/> 共享，因此同一路径不允许被任一存储类型的两个实例同时占用。
/// </para>
/// </remarks>
public sealed class JsonDocumentStore<T> : IAsyncDisposable where T : class, new()
{
    /// <summary>
    /// 默认的落盘子目录名。仅在未显式指定 <c>storageDirectory</c> 时使用；
    /// 显式传入目录时该目录本身即为文档文件所在目录，不会再追加此子目录。
    /// </summary>
    internal const string DataDirectoryName = "JsonDocumentStore";

    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly JsonDocumentFileHandler<T> _fileHandler;
    private readonly DirtyDebouncer _debouncer;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _filePath;

    // 保护文档的读写: Set/Update 替换引用, Read/SaveFile 取快照, 保证任一时刻看到完整文档
    private readonly object _gate = new();
    private T _document;
    private Action<Exception>? _logError;
    private int _disposed;

    /// <summary>
    /// 初始化 <see cref="JsonDocumentStore{T}"/> 的新实例。
    /// 从本地磁盘加载文档，并配置 5 秒延迟的防抖持久化任务。
    /// </summary>
    /// <param name="documentName">文档名称，用于区分不同的文档实例；文件名为 <c>{documentName}.json</c>。</param>
    /// <param name="typeInfo">
    /// 文档根类型的 JSON 类型信息，必须来自源生成上下文（如 <c>MyJsonContext.Default.MyDocument</c>）。
    /// 该实例同时决定序列化选项（缩进、编码器、注释处理等）。
    /// </param>
    /// <param name="logError">可选的异常日志记录回调，用于捕获后台落盘与损坏重建过程中的异常。</param>
    /// <param name="allowCorruptRebuild">是否允许在加载时遇到损坏文件时自动重建空文档并继续运行。默认为 <c>false</c>，即抛出异常。</param>
    /// <param name="storageDirectory">
    /// 文档文件所在目录；显式传入时该目录会被直接使用，不会再追加子目录。
    /// 默认为 <c>null</c>，此时使用 <c>{AppContext.BaseDirectory}/JsonDocumentStore</c>；
    /// 容器（需挂卷）、systemd 只读 root、安装在 <c>/usr/lib</c> 等场景应显式指定可写目录。
    /// 相对路径以 <see cref="AppContext.BaseDirectory"/> 为基准解析（不依赖进程当前目录），并在构造时立即转为绝对路径。
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="documentName"/> 为空或含非法字符，或 <paramref name="storageDirectory"/> 为空白字符串时抛出。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> 为 <c>null</c>，或 <paramref name="allowCorruptRebuild"/> 为 <c>true</c> 却未提供 <paramref name="logError"/> 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当解析后的文档文件路径已被其他实例占用时抛出。</exception>
    /// <exception cref="CacheFileCorruptException">当文档文件存在但内容损坏，且 <paramref name="allowCorruptRebuild"/> 为 <c>false</c> 时抛出。</exception>
    public JsonDocumentStore(
        string documentName,
        JsonTypeInfo<T> typeInfo,
        Action<Exception>? logError = null,
        bool allowCorruptRebuild = false,
        string? storageDirectory = null)
    {
        // 校验参数合法性
        ArgumentException.ThrowIfNullOrEmpty(documentName, nameof(documentName));
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (storageDirectory is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory, nameof(storageDirectory));
        if (allowCorruptRebuild && logError is null)
            throw new ArgumentNullException(nameof(logError), "allowCorruptRebuild 需要同时提供 logError, 否则损坏文件会被静默丢弃。");
        if (Path.GetInvalidFileNameChars().Any(c => documentName.Contains(c)))
            throw new ArgumentException("documentName 包含路径非法字符", nameof(documentName));

        // DataDirectoryName 只是缺省值: 显式传入目录时直接落盘到该目录
        string filePath = CacheFileRegistry.ResolveFile(documentName, DataDirectoryName, storageDirectory);

        // 与 CacheStore 共享注册表: 不同类型的存储也不能指向同一个文件, 否则会以不同格式互相覆盖
        if (!CacheFileRegistry.TryAcquire(filePath))
            throw new InvalidOperationException($"JSON 文档文件已被其他实例占用: {filePath}");

        // TryAcquire 成功之后, 任何失败都必须释放路径占位, 允许调用方修正后重试
        try
        {
            _filePath = filePath;
            _typeInfo = typeInfo;
            _logError = logError;

            _fileHandler = new JsonDocumentFileHandler<T>(filePath, typeInfo);

            try
            {
                _document = _fileHandler.LoadFromFile();
            }
            catch (CacheFileCorruptException ex) when (allowCorruptRebuild)
            {
                // 允许重建: 记录异常 + 把损坏文件改名留档(尽力而为) + 以空文档继续
                OnError(ex);
                try { _fileHandler.ArchiveCorruptFile(); }
                catch (Exception archiveEx) { OnError(archiveEx); }
                _document = new T();
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
            CacheFileRegistry.Release(filePath);
            throw;
        }
    }

    /// <summary>
    /// 获取当前文档的深拷贝快照。调用方可以直接使用或修改返回的实例，不会影响存储内部状态。
    /// </summary>
    /// <returns>文档的独立副本。</returns>
    /// <exception cref="ObjectDisposedException">实例已被释放。</exception>
    public T Read()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        lock (_gate)
            return Clone(_document);
    }

    /// <summary>
    /// 用指定的文档替换当前内容，并标记数据已更改以触发防抖落盘。
    /// </summary>
    /// <param name="value">新的文档内容；存储会保存其深拷贝，调用方后续修改该实例不会影响存储。</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 <c>null</c>。</exception>
    /// <exception cref="ObjectDisposedException">实例已被释放。</exception>
    /// <exception cref="JsonException">深拷贝序列化失败时抛出（此时存储内容保持不变，不会标记脏）。</exception>
    public void Set(T value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        ArgumentNullException.ThrowIfNull(value);

        // 先克隆再入账: 克隆失败时内存状态不变, 避免出现「已替换但未落盘」的半成品
        T snapshot = Clone(value);
        lock (_gate)
            _document = snapshot;

        _debouncer.MarkDirty();
    }

    /// <summary>
    /// 以原子方式更新文档：在锁内对当前文档的深拷贝执行 <paramref name="transform"/>，
    /// 变换成功后用结果替换当前文档并标记落盘；变换抛出异常时存储内容保持不变。
    /// </summary>
    /// <param name="transform">接收当前文档的独立副本并返回新文档的变换委托。</param>
    /// <exception cref="ArgumentNullException"><paramref name="transform"/> 为 <c>null</c>。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="transform"/> 返回 <c>null</c>。</exception>
    /// <exception cref="ObjectDisposedException">实例已被释放。</exception>
    /// <exception cref="JsonException">深拷贝序列化失败时抛出（此时存储内容保持不变，不会标记脏）。</exception>
    public void Update(Func<T, T> transform)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        ArgumentNullException.ThrowIfNull(transform);

        lock (_gate)
        {
            T current = Clone(_document);
            T next = transform(current)
                ?? throw new InvalidOperationException("transform 返回了 null。");
            _document = Clone(next);
        }

        _debouncer.MarkDirty();
    }

    /// <summary>
    /// 立即强制刷盘，不等待防抖倒计时。
    /// </summary>
    /// <returns>表示写入操作的任务。</returns>
    /// <exception cref="ObjectDisposedException">实例已被释放。</exception>
    public Task FlushAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        return _debouncer.ExecuteNowAsync();
    }

    /// <summary>
    /// 异步释放存储占用的资源。
    /// 会优先等待并确保当前未落盘的脏数据立即写入磁盘，随后释放定时器、取消令牌与文件路径占用。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        try
        {
            await _debouncer.ExecuteNowAsync().ConfigureAwait(false);
        }
        finally
        {
            _cts.Cancel();
            await _debouncer.DisposeAsync().ConfigureAwait(false);
            _cts.Dispose();
            CacheFileRegistry.Release(_filePath);
        }
    }

    /// <summary>
    /// 异步将内存中的文档序列化并写入本地 JSON 文件。
    /// </summary>
    /// <param name="ct">用于取消写入操作的取消令牌。</param>
    private async Task SaveFile(CancellationToken ct)
    {
        byte[] payload;
        lock (_gate)
        {
            // 在锁内生成快照: 序列化期间不允许 Set/Update 改树, 避免写出撕裂状态
            payload = JsonSerializer.SerializeToUtf8Bytes(_document, _typeInfo);
        }

        await _fileHandler.SaveToFileAsync(payload, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 通过 JSON 往返做深拷贝，与落盘格式保持一致。
    /// </summary>
    /// <param name="value">要克隆的文档。</param>
    /// <returns>独立副本。</returns>
    private T Clone(T value)
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(value, _typeInfo);
        return JsonSerializer.Deserialize(utf8, _typeInfo)
            ?? throw new InvalidOperationException("JSON 文档往返结果为 null。");
    }

    private void OnError(Exception ex)
    {
        _logError?.Invoke(ex);
    }
}
