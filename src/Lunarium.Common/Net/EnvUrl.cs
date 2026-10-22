namespace Lunarium.Common.Net;

/// <summary>
/// 保存从环境变量读取的 URL，并提供将其转换为 .NET 网络类型的辅助方法。
/// </summary>
/// <remarks>
/// 与 <see cref="ProxyUrl"/> 的区别只在语义：<see cref="ProxyUrl"/> 表示代理配置
/// （可转换为 <see cref="System.Net.IWebProxy"/>，且支持默认代理环境变量）；
/// <see cref="EnvUrl"/> 表示任意环境 URL，必须显式指定变量名，不包含代理相关转换。
/// </remarks>
public sealed record EnvUrl
{
    /// <summary>
    /// 获取此环境配置中保存的 URL。
    /// </summary>
    /// <value>环境 URL。</value>
    public string Value { get; }

    /// <summary>
    /// 初始化 <see cref="EnvUrl"/> 类的新实例。
    /// </summary>
    /// <param name="url">URL，不能为 <see langword="null"/>、空字符串或仅由空白字符组成。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="url"/> 为 <see langword="null"/> 时引发。</exception>
    /// <exception cref="ArgumentException">当 <paramref name="url"/> 为空或仅由空白字符组成时引发。</exception>
    public EnvUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Value = url;
    }

    /// <summary>
    /// 从指定环境变量创建 <see cref="EnvUrl"/> 实例。
    /// </summary>
    /// <param name="envVarName">要读取的环境变量名，必须显式指定（不同于 <see cref="ProxyUrl"/> 没有默认变量名）。</param>
    /// <returns>
    /// 环境变量值有效时返回新的 <see cref="EnvUrl"/> 实例；
    /// 未设置、为空或仅包含空白时返回 <see langword="null"/>。
    /// </returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="envVarName"/> 为 <see langword="null"/> 时引发。</exception>
    /// <exception cref="ArgumentException">当 <paramref name="envVarName"/> 为空或仅包含空白时引发。</exception>
    /// <remarks>
    /// 读取到的值会先裁剪首尾空白；"未设置"与"值为空白"无法区分，两者均返回 <see langword="null"/>。
    /// </remarks>
    public static EnvUrl? FromEnvironment(string envVarName)
    {
        var env = EnvVar.Get(envVarName);
        return env is null ? null : new EnvUrl(env.Value);
    }

    /// <summary>
    /// 将此环境 URL 解析为 <see cref="Uri"/> 实例。
    /// </summary>
    /// <returns>表示此环境 URL 的 <see cref="Uri"/> 实例。</returns>
    /// <exception cref="UriFormatException">环境 URL 不是有效的 URI。</exception>
    public Uri ToUri() => new(Value);

    /// <summary>
    /// 返回此环境配置中保存的 URL。
    /// </summary>
    /// <returns>环境 URL。</returns>
    public override string ToString() => Value;

    /// <summary>
    /// 将 <see cref="EnvUrl"/> 隐式转换为其保存的 URL 字符串。
    /// </summary>
    /// <param name="envUrl">要转换的环境 URL。</param>
    /// <returns>环境 URL。</returns>
    public static implicit operator string?(EnvUrl? envUrl) => envUrl?.Value;
}
