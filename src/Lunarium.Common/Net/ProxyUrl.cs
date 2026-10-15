using System.Net;

namespace Lunarium.Common.Net;

/// <summary>
/// 保存代理 URL，并提供将其转换为 .NET 网络类型的辅助方法
/// </summary>
public sealed record ProxyUrl
{
    /// <summary>
    /// 获取此代理配置中保存的 URL
    /// </summary>
    /// <value>代理 URL</value>
    public string Value { get; }

    /// <summary>
    /// 初始化 <see cref="ProxyUrl"/> 类的新实例
    /// </summary>
    /// <param name="url">代理 URL，不能为 <see langword="null"/>、空字符串或仅由空白字符组成</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="url"/> 为 <see langword="null"/> 时引发</exception>
    /// <exception cref="ArgumentException">当 <paramref name="url"/> 为空或仅由空白字符组成时引发</exception>
    public ProxyUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Value = url;
    }

    /// <summary>
    /// 从指定环境变量或默认代理环境变量创建 <see cref="ProxyUrl"/> 实例
    /// </summary>
    /// <param name="envVarName">
    /// 要读取的环境变量名为 <see langword="null"/> 时，先读取 <c>http_proxy</c>，
    /// 仅当其值为 <see langword="null"/> 时再读取 <c>HTTP_PROXY</c>
    /// </param>
    /// <returns>
    /// 环境变量值有效时返回新的 <see cref="ProxyUrl"/> 实例；
    /// 未设置、为空或仅包含空白时返回 <see langword="null"/>
    /// </returns>
    /// <exception cref="ArgumentException">
    /// 当 <paramref name="envVarName"/> 非 <see langword="null"/> 且为空或仅包含空白时引发
    /// </exception>
    /// <remarks>
    /// 读取到的值会先裁剪首尾空白；"未设置"与"值为空白"无法区分，两者均返回 <see langword="null"/>
    /// </remarks>
    public static ProxyUrl? FromEnvironment(string? envVarName = null)
    {
        var env = envVarName is null
            ? EnvVar.GetAny("http_proxy", "HTTP_PROXY")
            : EnvVar.Get(envVarName);

        return env is null ? null : new ProxyUrl(env.Value);
    }

    /// <summary>
    /// 使用此代理 URL 创建 .NET 标准的 <see cref="IWebProxy"/> 实例
    /// </summary>
    /// <returns>基于此代理 URL 创建的 <see cref="IWebProxy"/> 实例</returns>
    public IWebProxy ToWebProxy() => new WebProxy(Value);

    /// <summary>
    /// 将此代理 URL 解析为 <see cref="Uri"/> 实例
    /// </summary>
    /// <returns>表示此代理 URL 的 <see cref="Uri"/> 实例</returns>
    /// <exception cref="UriFormatException">代理 URL 不是有效的 URI</exception>
    public Uri ToUri() => new(Value);

    /// <summary>
    /// 返回此代理配置中保存的 URL
    /// </summary>
    /// <returns>代理 URL</returns>
    public override string ToString() => Value;

    /// <summary>
    /// 将 <see cref="ProxyUrl"/> 隐式转换为其保存的 URL 字符串
    /// </summary>
    /// <param name="proxyUrl">要转换的代理配置</param>
    /// <returns>代理 URL</returns>
    public static implicit operator string?(ProxyUrl? proxyUrl) => proxyUrl?.Value;
}