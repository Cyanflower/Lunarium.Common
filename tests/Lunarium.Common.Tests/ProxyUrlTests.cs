using System.Net;
using Lunarium.Common.Net;

namespace Lunarium.Common.Tests;

/// <summary>
/// <see cref="ProxyUrl"/> 的行为测试。
/// </summary>
/// <remarks>
/// 默认构造路径会读取 <c>http_proxy</c>/<c>HTTP_PROXY</c>，故本类与 <see cref="EnvVarTests"/>
/// 同处已禁用并行的 <see cref="EnvironmentVariableCollection"/>，并用 <see cref="EnvironmentScope"/>
/// 隔离副作用（CI 机器上可能本来就有代理变量）。
/// </remarks>
[Collection(EnvironmentVariableCollection.Name)]
public class ProxyUrlTests
{
    private const string LowerName = "http_proxy";
    private const string UpperName = "HTTP_PROXY";
    private const string CustomName = "LUNARIUM_TEST_PROXY";
    private const string Url = "http://user:pw@proxy.example:8080";

    /// <summary>
    /// 清空用到的变量并做快照，保证起点确定、结束后恢复。
    /// </summary>
    private static EnvironmentScope Scope()
    {
        Environment.SetEnvironmentVariable(LowerName, null);
        Environment.SetEnvironmentVariable(UpperName, null);
        Environment.SetEnvironmentVariable(CustomName, null);

        return new EnvironmentScope(LowerName, UpperName, CustomName);
    }

    [Fact]
    public void Constructor_KeepsUrl_AndRejectsNullOrBlank()
    {
        Assert.Equal(Url, new ProxyUrl(Url).Value);

        Assert.Throws<ArgumentNullException>(() => new ProxyUrl(null!));
        Assert.Throws<ArgumentException>(() => new ProxyUrl("   "));
    }

    [Fact]
    public void FromEnvironment_TrimsValue()
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(CustomName, "  " + Url + "  ");

        ProxyUrl? proxy = ProxyUrl.FromEnvironment(CustomName);

        Assert.NotNull(proxy);
        Assert.Equal(Url, proxy.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromEnvironment_ReturnsNull_ForBlankValue(string raw)
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(CustomName, raw);

        Assert.Null(ProxyUrl.FromEnvironment(CustomName));
    }

    [Fact]
    public void FromEnvironment_ReturnsNull_ForUnsetVariable()
    {
        using EnvironmentScope scope = Scope();

        Assert.Null(ProxyUrl.FromEnvironment(CustomName));
    }

    [Fact]
    public void FromEnvironment_RejectsBlankName()
    {
        Assert.Throws<ArgumentException>(() => ProxyUrl.FromEnvironment("   "));
    }

    [Fact]
    public void FromEnvironment_PrefersLowercaseHttpProxy()
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(LowerName, "http://lower:1");
        Environment.SetEnvironmentVariable(UpperName, "http://upper:1");

        Assert.Equal("http://lower:1", ProxyUrl.FromEnvironment()?.Value);
        // 显式传 null 也走默认路径
        Assert.Equal("http://lower:1", ProxyUrl.FromEnvironment(null)?.Value);
    }

    [Fact]
    public void FromEnvironment_FallsBackToUppercaseHttpProxy()
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(UpperName, "http://upper:1");

        Assert.Equal("http://upper:1", ProxyUrl.FromEnvironment()?.Value);
    }

    [Fact]
    public void FromEnvironment_ReturnsNull_WhenNeitherIsSet()
    {
        using EnvironmentScope scope = Scope();

        Assert.Null(ProxyUrl.FromEnvironment());
    }

    [Fact]
    public void ToUri_And_ToWebProxy_UseUrl()
    {
        var proxy = new ProxyUrl(Url);

        Uri uri = proxy.ToUri();
        Assert.Equal("proxy.example", uri.Host);
        Assert.Equal(8080, uri.Port);
        Assert.Equal("user:pw", uri.UserInfo);

        var webProxy = Assert.IsType<WebProxy>(proxy.ToWebProxy());
        Assert.Equal(uri, webProxy.Address);
    }

    [Fact]
    public void ToUri_Throws_ForInvalidUrl()
    {
        var proxy = new ProxyUrl("http://[invalid");

        Assert.Throws<UriFormatException>(() => proxy.ToUri());
    }

    [Fact]
    public void Stringification_IsConsistent()
    {
        var proxy = new ProxyUrl(Url);

        // ToString 与隐式转换目前都返回 URL；一旦给 ToString 脱敏，本断言会先失败，
        // 迫使在"删除隐式转换"与"同步两者"之间做决定（避免出现 EnvVar 那种双重转换分叉）。
        Assert.Equal(proxy.Value, proxy.ToString());
        Assert.Equal(proxy.Value, $"{proxy}");
        Assert.Equal(proxy.Value, "" + proxy);
        Assert.Equal(proxy.Value, string.Concat(proxy));
        Assert.Equal(proxy.Value, (string?)proxy);
    }
}
