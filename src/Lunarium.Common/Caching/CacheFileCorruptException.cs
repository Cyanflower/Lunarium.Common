namespace Lunarium.Common.Caching;

/// <summary>
/// 表示缓存文件存在但内容已损坏(无法解析)的异常。<br/>
/// 单独定义该类型, 便于调用方区分「文件损坏」与「IO 故障」等其它加载失败情形,
/// 也避免让上层依赖具体实现的异常包装约定。
/// </summary>
public sealed class CacheFileCorruptException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
