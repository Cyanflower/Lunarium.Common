using System.Globalization;

namespace Lunarium.Common;

/// <summary>
/// 表示一个环境变量，为读取、写入、删除与类型化解析提供统一入口
/// <see cref="Name"/> 与 <see cref="Value"/> 在构造时均会被裁剪首尾空白
/// </summary>
/// <remarks>
/// 读取语义：变量未设置，或其值为空/仅空白时 <see cref="Get"/> 一律返回 <see langword="null"/>，
/// 因此"未设置"与"被外部程序置为空白"无法区分
/// 写入语义：<see cref="EnvVar(string, string)"/> 与 <see cref="Set(string, string)"/> 都拒绝空白值，
/// 所以经由本类型写入的值必然非空；要清空变量必须用 <see cref="Unset(string)"/> 彻底删除，而不是写入空字符串
/// 名称的大小写敏感性随平台而异（Windows 不区分大小写）
/// 相等性按 <see cref="Name"/> 与 <see cref="Value"/> 比较（值语义）；<see cref="ToString"/> 只返回名称，不返回值
/// </remarks>
public sealed record EnvVar
{
    #region 属性与构造

    /// <summary>环境变量名</summary>
    public string Name { get; }

    /// <summary>环境变量值</summary>
    public string Value { get; }

    /// <summary>创建环境变量实例，名称与值都会被裁剪首尾空白</summary>
    /// <param name="name">环境变量名</param>
    /// <param name="value">环境变量值</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 或 <paramref name="value"/> 为 <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 或 <paramref name="value"/> 为空或仅包含空白</exception>
    public EnvVar(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Name = name.Trim();
        Value = value.Trim();
    }

    #endregion

    #region 读取

    /// <summary>读取指定名称的环境变量</summary>
    /// <param name="envVarName">环境变量名</param>
    /// <returns>变量实例；变量未设置或其值为空白时返回 <see langword="null"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="envVarName"/> 为 <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="envVarName"/> 为空或仅包含空白</exception>
    public static EnvVar? Get(string envVarName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envVarName);

        var value = Environment.GetEnvironmentVariable(envVarName);
        return string.IsNullOrWhiteSpace(value) ? null : new EnvVar(envVarName, value);
    }

    /// <summary>按顺序返回第一个已设置且值非空白的候选环境变量</summary>
    /// <param name="candidateNames">候选环境变量名，按优先级从高到低排列</param>
    /// <returns>第一个命中的变量实例；全部未命中时返回 <see langword="null"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidateNames"/> 或其任一元素为 <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="candidateNames"/> 为空数组，或其中任一元素为空或仅包含空白</exception>
    public static EnvVar? GetAny(params string[] candidateNames)
    {
        ArgumentNullException.ThrowIfNull(candidateNames);
        if (candidateNames.Length == 0)
            throw new ArgumentException("At least one candidate environment variable name must be provided.", nameof(candidateNames));

        foreach (var name in candidateNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var env = Get(name);
            if (env is not null) return env;
        }

        return null;
    }

    /// <summary>读取指定名称的环境变量，未设置或值为空白时抛出异常</summary>
    /// <param name="envVarName">环境变量名</param>
    /// <returns>变量实例</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envVarName"/> 为 <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="envVarName"/> 为空或仅包含空白</exception>
    /// <exception cref="InvalidOperationException">变量未设置或其值为空白</exception>
    public static EnvVar GetRequired(string envVarName)
    {
        return Get(envVarName)
            ?? throw new InvalidOperationException($"Required environment variable {envVarName} is not set or is empty.");
    }

    /// <summary>按顺序返回第一个已设置且值非空白的候选环境变量，全部未命中时抛出异常</summary>
    /// <param name="candidateNames">候选环境变量名，按优先级从高到低排列</param>
    /// <returns>第一个命中的变量实例</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidateNames"/> 或其任一元素为 <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="candidateNames"/> 为空数组，或其中任一元素为空或仅包含空白</exception>
    /// <exception cref="InvalidOperationException">全部候选变量都未设置或值为空白</exception>
    public static EnvVar GetAnyRequired(params string[] candidateNames)
    {
        return GetAny(candidateNames)
            ?? throw new InvalidOperationException(
                $"None of the candidate environment variables [{string.Join(", ", candidateNames)}] are set to a non-empty value.");
    }

    #endregion

    #region 写入与删除

    /// <summary>把指定名称与值写入进程环境变量，两者都会先裁剪首尾空白</summary>
    /// <param name="name">环境变量名</param>
    /// <param name="value">环境变量值</param>
    /// <returns>与写入内容一致的新实例（名称与值均已裁剪）</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 或 <paramref name="value"/> 为 <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 或 <paramref name="value"/> 为空或仅包含空白，或名称被平台拒绝（例如包含 '='）</exception>
    public static EnvVar Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedName = name.Trim();
        var trimmedValue = value.Trim();

        Environment.SetEnvironmentVariable(trimmedName, trimmedValue);
        return new EnvVar(trimmedName, trimmedValue);
    }

    /// <summary>把当前实例的 <see cref="Name"/> 与 <see cref="Value"/> 写入进程环境变量</summary>
    /// <returns>当前实例，便于链式调用</returns>
    public EnvVar Set()
    {
        Set(Name, Value);
        return this;
    }

    /// <summary>从进程环境中删除指定名称的环境变量，名称会先裁剪首尾空白</summary>
    /// <param name="name">环境变量名</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 为 <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或仅包含空白</exception>
    public static void Unset(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Environment.SetEnvironmentVariable(name.Trim(), null);
    }

    /// <summary>从进程环境中删除当前实例对应的环境变量</summary>
    public void Unset() => Unset(Name);

    #endregion

    #region 输出

    /// <summary>返回 <see cref="Name"/> 而不是变量的值，避免日志/异常信息意外泄露敏感值（取值请用 <see cref="Value"/>）</summary>
    /// <returns>环境变量名</returns>
    public override string ToString() => Name;

    #endregion

    #region 类型化解析（基于 .NET 7+ IParsable）

    /// <summary>把值解析为目标类型<typeparamref name="T"/> 为 <see cref="bool"/> 时使用宽松的布尔语义</summary>
    /// <typeparam name="T">目标类型，需实现 <c>IParsable&lt;T&gt;</c></typeparam>
    /// <param name="defaultValue">解析失败时返回的默认值</param>
    /// <returns>解析结果；失败时返回 <paramref name="defaultValue"/>，引用类型解析失败时返回 <see langword="null"/></returns>
    /// <remarks>
    /// <typeparamref name="T"/> 为 <see cref="bool"/> 时使用宽松字面量
    /// （<c>true</c>/<c>false</c>、<c>1</c>/<c>yes</c>/<c>on</c>、<c>0</c>/<c>no</c>/<c>off</c>，大小写不敏感），
    /// 因此 <c>As&lt;bool&gt;("1")</c> 为 <see langword="true"/>；
    /// 需要自定义真/假字面量时请使用 <see cref="AsBoolean(string, string, bool)"/>
    /// 其余类型一律使用严格的 <c>IParsable&lt;T&gt;.TryParse</c> 语义（固定不变文化）
    /// 枚举无法经由 <c>IParsable&lt;T&gt;</c> 解析，请改用 <see cref="AsEnum{TEnum}(TEnum, bool, bool)"/>
    /// </remarks>
    public T? As<T>(T? defaultValue = default) where T : IParsable<T>
    {
        return TryAs<T>(Value, out var result) ? result : defaultValue;
    }

    /// <summary>把值解析为目标类型，失败时抛出异常<typeparamref name="T"/> 为 <see cref="bool"/> 时使用宽松的布尔语义</summary>
    /// <typeparam name="T">目标类型，需实现 <c>IParsable&lt;T&gt;</c></typeparam>
    /// <returns>解析结果</returns>
    /// <remarks>
    /// <typeparamref name="T"/> 为 <see cref="bool"/> 时使用宽松字面量（<c>1</c>/<c>yes</c>/<c>on</c>、<c>0</c>/<c>no</c>/<c>off</c>），
    /// 因此 <c>RequiredAs&lt;bool&gt;("1")</c> 为 <see langword="true"/>、<c>RequiredAs&lt;bool&gt;("0")</c> 为 <see langword="false"/>；
    /// 其余类型使用严格的 <c>IParsable&lt;T&gt;.TryParse</c> 语义（固定不变文化）
    /// </remarks>
    /// <exception cref="InvalidOperationException">值无法转换为 <typeparamref name="T"/></exception>
    public T RequiredAs<T>() where T : IParsable<T>
    {
        if (TryAs<T>(Value, out var result))
        {
            return result;
        }

        throw new InvalidOperationException(
            $"Environment variable {Name} cannot be converted to {typeof(T).Name}.");
    }

    #endregion

    #region 布尔解析（自定义真/假字面量）

    /// <summary>把值解析为 <see cref="bool"/>，除 <c>true</c>/<c>false</c> 外还接受自定义的真/假字面量（大小写不敏感）不需要自定义字面量时请直接用 <c>As&lt;bool&gt;()</c></summary>
    /// <param name="trueName">视为 <see langword="true"/> 的自定义字面量</param>
    /// <param name="falseName">视为 <see langword="false"/> 的自定义字面量</param>
    /// <param name="defaultValue">解析失败时返回的默认值</param>
    /// <returns>解析结果；失败时返回 <paramref name="defaultValue"/></returns>
    public bool AsBoolean(string trueName, string falseName, bool defaultValue = false) =>
        TryParseBooleanCustom(Value, trueName, falseName, out var result) ? result : defaultValue;

    /// <summary>把值解析为 <see cref="bool"/>（支持自定义真/假字面量），失败时抛出异常不需要自定义字面量时请直接用 <c>RequiredAs&lt;bool&gt;()</c></summary>
    /// <param name="trueName">视为 <see langword="true"/> 的自定义字面量</param>
    /// <param name="falseName">视为 <see langword="false"/> 的自定义字面量</param>
    /// <returns>解析结果</returns>
    /// <exception cref="InvalidOperationException">值无法解析为 <see cref="bool"/></exception>
    public bool RequiredAsBoolean(string trueName, string falseName)
    {
        if (TryParseBooleanCustom(Value, trueName, falseName, out var result)) return result;
        throw new InvalidOperationException($"Environment variable {Name} cannot be converted to Boolean.");
    }

    #endregion

    #region 枚举解析

    /// <summary>把值解析为枚举</summary>
    /// <typeparam name="TEnum">枚举类型</typeparam>
    /// <param name="defaultValue">解析失败或被 <paramref name="requireDefined"/> 拒绝时返回的默认值</param>
    /// <param name="ignoreCase">是否忽略大小写</param>
    /// <param name="requireDefined">是否要求结果必须是已定义的枚举成员设为 <see langword="false"/> 时，纯数字字符串（如 <c>"99"</c>）会被接受为未定义值</param>
    /// <returns>解析结果；失败时返回 <paramref name="defaultValue"/></returns>
    public TEnum AsEnum<TEnum>(TEnum defaultValue = default, bool ignoreCase = true, bool requireDefined = false) where TEnum : struct, Enum
    {
        return TryParseEnum<TEnum>(Value, ignoreCase, requireDefined, out var result, out _) ? result : defaultValue;
    }

    /// <summary>把值解析为枚举，解析失败或结果不是已定义成员时抛出异常</summary>
    /// <typeparam name="TEnum">枚举类型</typeparam>
    /// <param name="ignoreCase">是否忽略大小写</param>
    /// <param name="requireDefined">是否要求结果必须是已定义的枚举成员</param>
    /// <returns>解析结果</returns>
    /// <exception cref="InvalidOperationException">值无法解析为 <typeparamref name="TEnum"/>，或不是已定义的枚举成员</exception>
    public TEnum RequiredAsEnum<TEnum>(bool ignoreCase = true, bool requireDefined = true) where TEnum : struct, Enum
    {
        if (TryParseEnum<TEnum>(Value, ignoreCase, requireDefined, out var result, out var isUndefined))
        {
            return result;
        }

        throw new InvalidOperationException(isUndefined
            ? $"Environment variable {Name} is not a defined member of {typeof(TEnum).Name}."
            : $"Environment variable {Name} cannot be converted to {typeof(TEnum).Name}.");
    }

    #endregion

    #region 内部辅助解析器

    // 统一解析入口：T 为 bool 时改走宽松解析器（与 AsBoolean 完全一致），其余类型走 IParsable<T>
    // typeof(T) == typeof(bool) 对每个值类型实参都是编译期常量，JIT 可折叠该分支；无反射，AOT 安全
    private static bool TryAs<T>(string value, out T result) where T : IParsable<T>
    {
        if (typeof(T) == typeof(bool))
        {
            // 只有 T 恰为 bool 时才会进入此分支，故下面的装箱/拆箱转换必然成立
            if (TryParseBoolean(value, out var boolean))
            {
                result = (T)(object)boolean;
                return true;
            }

            result = default!;
            return false;
        }

        if (T.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = default!;
        return false;
    }

    // 枚举解析：无法解析返回 false；解析成功但（requireDefined 时）不在已定义成员内时 isUndefined 为 true
    private static bool TryParseEnum<TEnum>(string value, bool ignoreCase, bool requireDefined, out TEnum result, out bool isUndefined)
        where TEnum : struct, Enum
    {
        isUndefined = false;

        if (!Enum.TryParse<TEnum>(value, ignoreCase, out var parsed))
        {
            result = default;
            return false;
        }

        if (requireDefined && !Enum.IsDefined(parsed))
        {
            result = default;
            isUndefined = true;
            return false;
        }

        result = parsed;
        return true;
    }

    // 宽松布尔字面量 = bool.TryParse 认的 true/false，加上 yes/no、on/off、1/0 三组（大小写不敏感）
    private static bool TryParseBoolean(string value, out bool result)
    {
        if (bool.TryParse(value, out result)) return true;
        if (TryParseBooleanCustom(value, "yes", "no", out result)) return true;
        if (TryParseBooleanCustom(value, "on", "off", out result)) return true;
        return TryParseBooleanCustom(value, "1", "0", out result);
    }

    private static bool TryParseBooleanCustom(string value, string trueName, string falseName, out bool result)
    {
        if (bool.TryParse(value, out result)) return true;
        if (value.Equals(trueName, StringComparison.OrdinalIgnoreCase)) { result = true; return true; }
        if (value.Equals(falseName, StringComparison.OrdinalIgnoreCase)) { result = false; return true; }

        result = false;
        return false;
    }

    #endregion
}
