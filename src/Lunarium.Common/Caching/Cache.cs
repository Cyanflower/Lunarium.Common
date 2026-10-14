using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace Lunarium.Common.Caching;

/// <summary>
/// 强类型标量键值缓存容器：把值按类型分入 5 个并发字典桶，并用一个不参与序列化的私有索引
/// 保证「一个键只落在一个桶」.
/// </summary>
/// <remarks>
/// 桶属性为公开可写（便于 JSON 反序列化），但绕过 <see cref="Set{T}"/> 直接写入桶后，
/// 必须调用 <see cref="RebuildIndex"/> 重建索引，否则 <see cref="Get{T}"/> / <see cref="Remove"/> 无法定位该键。
/// </remarks>
public class Cache
{
    /// <summary>标量值所属的存储桶。</summary>
    private enum BucketKind : byte
    {
        String,
        Number,
        UnsignedNumber,
        FloatingNumber,
        Boolean
    }

    // 键 → 所在桶。私有派生状态: 不参与 JSON 序列化(加载后由 RebuildIndex 重建),
    // 用来保证「一个键只落在一个桶」并让 Get/Remove 精确定位, 避免跨桶重复与残留。
    [JsonIgnore]
    private readonly ConcurrentDictionary<string, BucketKind> _index = new();

    // 保护 Set/Remove 的多步改动(ConcurrentDictionary 只保证单步原子, 索引与桶需要保持一致)。
    private readonly object _gate = new();

    /// <summary>字符串桶：键 → <see cref="string"/> 值。</summary>
    public ConcurrentDictionary<string, string> Strings { get; set; } = new();

    /// <summary>有符号整数桶：键 → <see cref="long"/> 值（<see cref="int"/>、<see cref="short"/> 也归入此桶）。</summary>
    public ConcurrentDictionary<string, long> Numbers { get; set; } = new();

    /// <summary>无符号整数桶：键 → <see cref="ulong"/> 值（<see cref="uint"/> 也归入此桶）。</summary>
    public ConcurrentDictionary<string, ulong> UnsignedNumbers { get; set; } = new();

    /// <summary>浮点桶：键 → <see cref="double"/> 值（<see cref="float"/> 也归入此桶）。</summary>
    public ConcurrentDictionary<string, double> FloatingNumbers { get; set; } = new();

    /// <summary>布尔桶：键 → <see cref="bool"/> 值。</summary>
    public ConcurrentDictionary<string, bool> Booleans { get; set; } = new();

    /// <summary>
    /// 写入或更新一个键值。若该键原先落在别的桶（值类型变了），会先清除旧桶中的同名项，
    /// 保证一个键不会同时存在于多个桶。
    /// </summary>
    /// <typeparam name="T">
    /// 值类型，仅支持：<see cref="string"/>、<see cref="int"/>、<see cref="long"/>、<see cref="short"/>、
    /// <see cref="uint"/>、<see cref="ulong"/>、<see cref="double"/>、<see cref="float"/>、<see cref="bool"/>。
    /// </typeparam>
    /// <param name="key">键名。</param>
    /// <param name="value">要写入的值。</param>
    /// <exception cref="NotSupportedException"><paramref name="value"/> 的类型不在上述支持列表内时抛出。</exception>
    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            var kind = KindOf(value, typeof(T).Name);

            // 类型变了 → 先清掉旧桶里的同键值, 避免一个键同时落在多个桶
            if (_index.TryGetValue(key, out var previous) && previous != kind)
                RemoveFromBucket(previous, key);

            WriteToBucket(kind, key, value);
            _index[key] = kind;
        }
    }

    /// <summary>
    /// 读取指定键的强类型值。
    /// </summary>
    /// <typeparam name="T">期望的目标类型；需与写入时归入的桶一致，否则视为未命中。</typeparam>
    /// <param name="key">键名。</param>
    /// <param name="defaultValue">未命中或类型不匹配时的回退值。</param>
    /// <returns>命中则返回缓存值，否则返回 <paramref name="defaultValue"/>。</returns>
    public T? Get<T>(string key, T? defaultValue = default)
    {
        // 未登记, 或请求类型与所存桶不符 → 直接返回默认值(无需逐桶探测)
        if (!_index.TryGetValue(key, out var kind) || !TryGetKind(typeof(T), out var wanted) || kind != wanted)
            return defaultValue;

        switch (kind)
        {
            case BucketKind.String:
                return Strings.TryGetValue(key, out var s) ? (T)(object)s : defaultValue;
            case BucketKind.Number:
                if (!Numbers.TryGetValue(key, out var l)) return defaultValue;
                if (typeof(T) == typeof(long)) return (T)(object)l;
                if (typeof(T) == typeof(int)) return (T)(object)(int)l;
                if (typeof(T) == typeof(short)) return (T)(object)(short)l;
                return defaultValue;
            case BucketKind.UnsignedNumber:
                if (!UnsignedNumbers.TryGetValue(key, out var ul)) return defaultValue;
                if (typeof(T) == typeof(ulong)) return (T)(object)ul;
                if (typeof(T) == typeof(uint)) return (T)(object)(uint)ul;
                return defaultValue;
            case BucketKind.FloatingNumber:
                if (!FloatingNumbers.TryGetValue(key, out var d)) return defaultValue;
                if (typeof(T) == typeof(double)) return (T)(object)d;
                if (typeof(T) == typeof(float)) return (T)(object)(float)d;
                return defaultValue;
            case BucketKind.Boolean:
                return Booleans.TryGetValue(key, out var b) ? (T)(object)b : defaultValue;
            default:
                return defaultValue;
        }
    }

    /// <summary>
    /// 删除指定键（按索引精确定位到唯一桶）。键不存在或已被移除时返回 <see langword="false"/>。
    /// </summary>
    public bool Remove(string key)
    {
        lock (_gate)
        {
            if (!_index.TryRemove(key, out var kind))
                return false;

            RemoveFromBucket(kind, key);
            return true;
        }
    }

    /// <summary>
    /// 依据各桶的实际内容重建「键 → 桶」索引。<br/>
    /// 索引是私有派生状态、不参与 JSON 序列化, 因此<strong>反序列化加载后必须调用一次</strong>;
    /// 若绕过 <see cref="Set{T}"/> 直接写入公开的桶字典, 也应调用它以恢复一致。
    /// 同一键若存在于多个桶, 按 String → Number → UnsignedNumber → FloatingNumber → Boolean 的顺序保留第一个,
    /// 其余重复项(旧版本可能留下的脏数据)会被清除。
    /// </summary>
    public void RebuildIndex()
    {
        lock (_gate)
        {
            _index.Clear();
            foreach (var key in Strings.Keys) _index[key] = BucketKind.String;
            foreach (var key in Numbers.Keys) Claim(key, BucketKind.Number, Numbers);
            foreach (var key in UnsignedNumbers.Keys) Claim(key, BucketKind.UnsignedNumber, UnsignedNumbers);
            foreach (var key in FloatingNumbers.Keys) Claim(key, BucketKind.FloatingNumber, FloatingNumbers);
            foreach (var key in Booleans.Keys) Claim(key, BucketKind.Boolean, Booleans);
        }
    }

    // 认领键; 若已被优先级更高的桶占用, 则把当前这项作为重复脏数据移除
    private void Claim<TValue>(string key, BucketKind kind, ConcurrentDictionary<string, TValue> bucket)
    {
        if (!_index.TryAdd(key, kind))
            bucket.TryRemove(key, out _);
    }

    // 判定值所属桶; 不受支持的类型抛 NotSupportedException
    private static BucketKind KindOf(object? value, string typeName) => value switch
    {
        string => BucketKind.String,
        long or int or short => BucketKind.Number,
        ulong or uint => BucketKind.UnsignedNumber,
        double or float => BucketKind.FloatingNumber,
        bool => BucketKind.Boolean,
        _ => throw new NotSupportedException($"不支持的缓存类型: {typeName}")
    };

    // 类型 → 桶; 返回 false 表示该类型不受支持
    private static bool TryGetKind(Type type, out BucketKind kind)
    {
        if (type == typeof(string)) { kind = BucketKind.String; return true; }
        if (type == typeof(long) || type == typeof(int) || type == typeof(short)) { kind = BucketKind.Number; return true; }
        if (type == typeof(ulong) || type == typeof(uint)) { kind = BucketKind.UnsignedNumber; return true; }
        if (type == typeof(double) || type == typeof(float)) { kind = BucketKind.FloatingNumber; return true; }
        if (type == typeof(bool)) { kind = BucketKind.Boolean; return true; }
        kind = default;
        return false;
    }

    private void WriteToBucket(BucketKind kind, string key, object? value)
    {
        switch (kind)
        {
            case BucketKind.String: Strings[key] = (string)value!; break;
            case BucketKind.Number: Numbers[key] = Convert.ToInt64(value); break;
            case BucketKind.UnsignedNumber: UnsignedNumbers[key] = Convert.ToUInt64(value); break;
            case BucketKind.FloatingNumber: FloatingNumbers[key] = Convert.ToDouble(value); break;
            case BucketKind.Boolean: Booleans[key] = (bool)value!; break;
        }
    }

    private bool RemoveFromBucket(BucketKind kind, string key) => kind switch
    {
        BucketKind.String => Strings.TryRemove(key, out _),
        BucketKind.Number => Numbers.TryRemove(key, out _),
        BucketKind.UnsignedNumber => UnsignedNumbers.TryRemove(key, out _),
        BucketKind.FloatingNumber => FloatingNumbers.TryRemove(key, out _),
        BucketKind.Boolean => Booleans.TryRemove(key, out _),
        _ => false
    };
}