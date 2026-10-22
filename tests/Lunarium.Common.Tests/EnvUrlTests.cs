using Lunarium.Common.Net;

namespace Lunarium.Common.Tests;

/// <summary>
/// <see cref="EnvUrl"/> 的行为测试。
/// </summary>
/// <remarks>
/// <see cref="EnvUrl.FromEnvironment"/> 会读写进程级环境变量，故与 <see cref="EnvVarTests"/>
/// 同处已禁用并行的 <see cref="EnvironmentVariableCollection"/>，并用 <see cref="EnvironmentScope"/>
/// 隔离副作用。
/// </remarks>
[Collection(EnvironmentVariableCollection.Name)]
public class EnvUrlTests
{
    private const string CustomName = "LUNARIUM_TEST_ENV_URL";
    private const string Url = "https://api.example.com:8443/base?q=1";

    /// <summary>
    /// 先快照再清空变量，保证起点确定、结束后恢复宿主机原值。
    /// </summary>
    private static EnvironmentScope Scope()
    {
        var scope = new EnvironmentScope(CustomName);
        Environment.SetEnvironmentVariable(CustomName, null);
        return scope;
    }

    [Fact]
    public void Constructor_KeepsUrl_AndRejectsNullOrBlank()
    {
        Assert.Equal(Url, new EnvUrl(Url).Value);

        Assert.Throws<ArgumentNullException>(() => new EnvUrl(null!));
        Assert.Throws<ArgumentException>(() => new EnvUrl("   "));
    }

    [Fact]
    public void FromEnvironment_TrimsValue()
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(CustomName, "  " + Url + "  ");

        EnvUrl? envUrl = EnvUrl.FromEnvironment(CustomName);

        Assert.NotNull(envUrl);
        Assert.Equal(Url, envUrl.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromEnvironment_ReturnsNull_ForBlankValue(string raw)
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(CustomName, raw);

        Assert.Null(EnvUrl.FromEnvironment(CustomName));
    }

    [Fact]
    public void FromEnvironment_ReturnsNull_ForUnsetVariable()
    {
        using EnvironmentScope scope = Scope();

        Assert.Null(EnvUrl.FromEnvironment(CustomName));
    }

    [Fact]
    public void FromEnvironment_RejectsNullOrBlankName()
    {
        Assert.Throws<ArgumentNullException>(() => EnvUrl.FromEnvironment(null!));
        Assert.Throws<ArgumentException>(() => EnvUrl.FromEnvironment("   "));
    }

    [Fact]
    public void ToUri_UsesUrl()
    {
        var envUrl = new EnvUrl(Url);

        Uri uri = envUrl.ToUri();

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("api.example.com", uri.Host);
        Assert.Equal(8443, uri.Port);
        Assert.Equal("/base", uri.AbsolutePath);
        Assert.Equal("?q=1", uri.Query);
    }

    [Fact]
    public void ToUri_Throws_ForInvalidUrl()
    {
        var envUrl = new EnvUrl("http://[invalid");

        Assert.Throws<UriFormatException>(() => envUrl.ToUri());
    }

    [Fact]
    public void Stringification_IsConsistent()
    {
        var envUrl = new EnvUrl(Url);

        Assert.Equal(envUrl.Value, envUrl.ToString());
        Assert.Equal(envUrl.Value, $"{envUrl}");
        Assert.Equal(envUrl.Value, "" + envUrl);
        Assert.Equal(envUrl.Value, string.Concat(envUrl));
        Assert.Equal(envUrl.Value, (string?)envUrl);
    }

    [Fact]
    public void RecordEquality_ComparesValue()
    {
        Assert.Equal(new EnvUrl(Url), new EnvUrl(Url));
        Assert.NotEqual(new EnvUrl(Url), new EnvUrl(Url + "&x=1"));
    }
}
