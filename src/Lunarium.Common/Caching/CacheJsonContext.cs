using System.Text.Json.Serialization;

namespace Lunarium.Common.Caching;

/// <summary>
/// <see cref="Cache"/> 的 JSON 源生成上下文。
/// </summary>
/// <remarks>
/// 把 <c>JsonSerializer.Serialize/Deserialize</c> 的调用从「运行时反射生成元数据」切换到
/// 「编译期静态生成的元数据」，从而同时消除裁剪告警 (IL2026) 与 Native AOT 告警 (IL3050)，
/// 并避免在 AOT 应用中出现「编译期无告警、运行时 <see cref="System.Text.Json"/> 反射路径被禁用」的静默失败。
/// <para>
/// 注意：<see cref="JsonSourceGenerationOptionsAttribute"/> 没有 <c>Encoder</c> 参数，
/// 「非 ASCII 不转义」只能通过构造函数的 <see cref="System.Text.Json.JsonSerializerOptions"/> 实例传入，
/// 且该实例需静态复用（上下文按 options 实例缓存元数据，首次使用后不可再修改）。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Cache))]
internal partial class CacheJsonContext : JsonSerializerContext;
