using System.Globalization;

namespace Lunarium.Common.Tests;

/// <summary>
/// <see cref="EnvVar"/> 的行为测试。
/// </summary>
/// <remarks>
/// 环境变量是进程级全局状态，故本类挂到已禁用并行的 <see cref="EnvironmentVariableCollection"/>，
/// 并用 <see cref="EnvironmentScope"/> 在每个用例结束后把变量恢复原状。
/// </remarks>
[Collection(EnvironmentVariableCollection.Name)]
public class EnvVarTests
{
    private const string NameA = "LUNARIUM_TEST_ENVVAR_A";
    private const string NameB = "LUNARIUM_TEST_ENVVAR_B";
    private const string MissingName = "LUNARIUM_TEST_ENVVAR_MISSING";
    private const string SecretName = "LUNARIUM_TEST_ENVVAR_SECRET";
    private const string Secret = "s3cr3t-value";

    /// <summary>
    /// 建一个确定性的起点：先把用到的变量全部清空，再对它们做快照以便结束后恢复。
    /// </summary>
    private static EnvironmentScope Scope()
    {
        Environment.SetEnvironmentVariable(NameA, null);
        Environment.SetEnvironmentVariable(NameB, null);
        Environment.SetEnvironmentVariable(MissingName, null);
        Environment.SetEnvironmentVariable(SecretName, null);

        return new EnvironmentScope(NameA, NameB, MissingName, SecretName);
    }

    // ==== 属性与构造 ====

    [Fact]
    public void Constructor_TrimsNameAndValue()
    {
        var env = new EnvVar("  NAME  ", "  value  ");

        Assert.Equal("NAME", env.Name);
        Assert.Equal("value", env.Value);
    }

    [Fact]
    public void Constructor_RejectsNullOrBlank()
    {
        Assert.Throws<ArgumentNullException>(() => new EnvVar(null!, "value"));
        Assert.Throws<ArgumentNullException>(() => new EnvVar("NAME", null!));
        Assert.Throws<ArgumentException>(() => new EnvVar("  ", "value"));
        Assert.Throws<ArgumentException>(() => new EnvVar("NAME", "  "));
    }

    // ==== 读取 ====

    [Fact]
    public void Get_ReturnsInstance_WithTrimmedValue()
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(NameA, "  1  ");

        EnvVar? env = EnvVar.Get(NameA);

        Assert.NotNull(env);
        Assert.Equal(NameA, env.Name);
        Assert.Equal("1", env.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Get_TreatsBlankValueAsUnset(string raw)
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(NameA, raw);

        Assert.Null(EnvVar.Get(NameA));
    }

    [Fact]
    public void Get_ReturnsNull_ForUnsetVariable()
    {
        using EnvironmentScope scope = Scope();

        Assert.Null(EnvVar.Get(MissingName));
    }

    [Fact]
    public void Get_RejectsNullOrBlankName()
    {
        Assert.Throws<ArgumentNullException>(() => EnvVar.Get(null!));
        Assert.Throws<ArgumentException>(() => EnvVar.Get("   "));
    }

    [Fact]
    public void GetAny_ReturnsFirstSetCandidate()
    {
        using EnvironmentScope scope = Scope();

        Assert.Null(EnvVar.GetAny(NameA, NameB, MissingName));

        Environment.SetEnvironmentVariable(NameB, "b");
        Assert.Equal("b", EnvVar.GetAny(NameA, NameB, MissingName)?.Value);

        Environment.SetEnvironmentVariable(NameA, "a");
        Assert.Equal("a", EnvVar.GetAny(NameA, NameB, MissingName)?.Value);
    }

    [Fact]
    public void GetAny_RejectsNullArrayEmptyArrayAndInvalidCandidates()
    {
        using EnvironmentScope scope = Scope();

        Assert.Throws<ArgumentNullException>(() => EnvVar.GetAny(null!));
        Assert.Throws<ArgumentException>(() => EnvVar.GetAny());
        Assert.Throws<ArgumentNullException>(() => EnvVar.GetAny(MissingName, null!));
        Assert.Throws<ArgumentException>(() => EnvVar.GetAny(MissingName, "   "));
    }

    [Fact]
    public void GetRequired_ReturnsInstance_OrThrows()
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(NameA, " 1 ");

        Assert.Equal("1", EnvVar.GetRequired(NameA).Value);
        Assert.Throws<InvalidOperationException>(() => EnvVar.GetRequired(MissingName));

        // 值为空白时与"未设置"同等看待
        Environment.SetEnvironmentVariable(NameA, "   ");
        Assert.Throws<InvalidOperationException>(() => EnvVar.GetRequired(NameA));
    }

    [Fact]
    public void GetAnyRequired_ReturnsFirstSetCandidate_OrThrows()
    {
        using EnvironmentScope scope = Scope();

        Assert.Throws<InvalidOperationException>(() => EnvVar.GetAnyRequired(NameA, NameB));
        Assert.Throws<ArgumentException>(() => EnvVar.GetAnyRequired());

        Environment.SetEnvironmentVariable(NameB, "b");
        Assert.Equal("b", EnvVar.GetAnyRequired(NameA, NameB).Value);
    }

    // ==== 写入与删除 ====

    [Fact]
    public void Set_WritesTrimmedNameAndValue_AndReturnsMatchingInstance()
    {
        using EnvironmentScope scope = Scope();

        EnvVar env = EnvVar.Set(" " + NameA + " ", "  1  ");

        Assert.Equal(NameA, env.Name);
        Assert.Equal("1", env.Value);
        Assert.Equal("1", Environment.GetEnvironmentVariable(NameA));
        // 不会写出一个带空格的变量名
        Assert.Null(Environment.GetEnvironmentVariable(" " + NameA + " "));
    }

    [Fact]
    public void Set_RejectsNullOrBlank()
    {
        using EnvironmentScope scope = Scope();

        Assert.Throws<ArgumentNullException>(() => EnvVar.Set(null!, "value"));
        Assert.Throws<ArgumentNullException>(() => EnvVar.Set(NameA, null!));
        Assert.Throws<ArgumentException>(() => EnvVar.Set("  ", "value"));
        Assert.Throws<ArgumentException>(() => EnvVar.Set(NameA, "  "));
    }

    [Fact]
    public void Set_Throws_WhenPlatformRejectsName()
    {
        using EnvironmentScope scope = Scope();

        // 名称含 '=' 在 Windows/Linux 上都会被平台拒绝（Linux 抛 ArgumentException）
        Assert.ThrowsAny<ArgumentException>(() => EnvVar.Set("A=B", "value"));
    }

    [Fact]
    public void Unset_RemovesVariable_AndTrimsName()
    {
        using EnvironmentScope scope = Scope();
        EnvVar.Set(NameA, "1");

        EnvVar.Unset(" " + NameA + " ");

        Assert.Null(Environment.GetEnvironmentVariable(NameA));
    }

    [Fact]
    public void Unset_RejectsNullOrBlank()
    {
        Assert.Throws<ArgumentNullException>(() => EnvVar.Unset(null!));
        Assert.Throws<ArgumentException>(() => EnvVar.Unset("  "));
    }

    [Fact]
    public void InstanceSetAndUnset_RoundTrip()
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(NameA, "1");

        env.Unset();
        Assert.Null(Environment.GetEnvironmentVariable(NameA));

        Assert.Same(env, env.Set());
        Assert.Equal(env.Value, Environment.GetEnvironmentVariable(NameA));
    }

    // ==== 输出与脱敏 ====

    [Fact]
    public void ToString_ReturnsName_AndDoesNotLeakValue()
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(SecretName, Secret);

        Assert.Equal(SecretName, env.ToString());
        Assert.DoesNotContain(Secret, env.ToString());
        Assert.Equal(Secret, env.Value);
    }

    [Fact]
    public void ExceptionMessages_DoNotLeakValue()
    {
        using EnvironmentScope scope = Scope();

        InvalidOperationException asError =
            Assert.Throws<InvalidOperationException>(() => EnvVar.Set(SecretName, Secret).RequiredAs<int>());
        Assert.Contains(SecretName, asError.Message);
        Assert.DoesNotContain(Secret, asError.Message);

        InvalidOperationException boolError =
            Assert.Throws<InvalidOperationException>(() => EnvVar.Set(SecretName, Secret).RequiredAsBoolean("enabled", "disabled"));
        Assert.DoesNotContain(Secret, boolError.Message);

        InvalidOperationException enumError =
            Assert.Throws<InvalidOperationException>(() => EnvVar.Set(SecretName, Secret).RequiredAsEnum<TestMode>());
        Assert.DoesNotContain(Secret, enumError.Message);
    }

    // ==== 相等性（record 值语义） ====

    [Fact]
    public void Equality_IsValueBased()
    {
        using EnvironmentScope scope = Scope();
        Environment.SetEnvironmentVariable(NameA, "1");

        EnvVar first = EnvVar.GetRequired(NameA);
        EnvVar second = EnvVar.GetRequired(NameA);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        Environment.SetEnvironmentVariable(NameA, "2");
        Assert.NotEqual(first, EnvVar.GetRequired(NameA));
    }

    // ==== 类型化解析：泛型 ====

    [Fact]
    public void As_ParsesParsableTypes_WithInvariantCulture()
    {
        using EnvironmentScope scope = Scope();

        Assert.Equal(5, EnvVar.Set(NameA, "+5").As<int>());
        Assert.Equal(42L, EnvVar.Set(NameA, "42").As<long>());
        Assert.Equal(1.5, EnvVar.Set(NameA, "1.5").As<double>());
        Assert.Equal(TimeSpan.FromMinutes(3), EnvVar.Set(NameA, "00:03:00").As<TimeSpan>());

        Guid guid = Guid.NewGuid();
        Assert.Equal(guid, EnvVar.Set(NameA, guid.ToString()).As<Guid>());
    }

    [Fact]
    public void As_ReturnsDefault_OrNull_WhenUnparsable()
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(NameA, "abc");

        Assert.Equal(0, env.As<int>());
        Assert.Equal(7, env.As<int>(7));
        // 引用类型解析失败时返回 null（UnparsableWidget 以 '!' 开头视为失败）
        Assert.Null(EnvVar.Set(NameA, "!bad").As<UnparsableWidget>());
        Assert.Equal("ok", EnvVar.Set(NameA, "ok").As<UnparsableWidget>()?.Raw);
    }

    [Fact]
    public void RequiredAs_ReturnsValue_OrThrows()
    {
        using EnvironmentScope scope = Scope();

        Assert.Equal(3, EnvVar.Set(NameA, "3").RequiredAs<int>());
        Assert.Throws<InvalidOperationException>(() => EnvVar.Set(NameA, "abc").RequiredAs<int>());
    }

    // ==== 类型化解析：bool ====

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData("on")]
    [InlineData("ON")]
    public void AsBool_TreatsLenientTruthyLiteralsAsTrue(string raw)
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(NameA, raw);

        Assert.True(env.As<bool>());
        Assert.True(env.RequiredAs<bool>());
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("off")]
    [InlineData("OFF")]
    public void AsBool_TreatsLenientFalsyLiteralsAsFalse(string raw)
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(NameA, raw);

        Assert.False(env.As<bool>());
        Assert.False(env.RequiredAs<bool>());
    }

    [Fact]
    public void AsBool_ReturnsDefault_ForUnparsable()
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(NameA, "maybe");

        Assert.False(env.As<bool>());
        Assert.True(env.As<bool>(true));
        Assert.Throws<InvalidOperationException>(() => env.RequiredAs<bool>());
    }

    // ==== 类型化解析：bool 自定义字面量 ====

    [Fact]
    public void AsBoolean_CustomLiterals_ExcludeBuiltInAliases()
    {
        using EnvironmentScope scope = Scope();

        Assert.True(EnvVar.Set(NameA, "enabled").AsBoolean("enabled", "disabled"));
        Assert.False(EnvVar.Set(NameA, "DISABLED").AsBoolean("enabled", "disabled"));
        // BCL 的 true/false 依然被接受
        Assert.True(EnvVar.Set(NameA, "TRUE").AsBoolean("enabled", "disabled"));

        // 1/yes/on 这类别名不在自定义字面量集合内 => 回退默认值
        EnvVar aliased = EnvVar.Set(NameA, "1");
        Assert.False(aliased.AsBoolean("enabled", "disabled"));
        Assert.True(aliased.AsBoolean("enabled", "disabled", defaultValue: true));
    }

    [Fact]
    public void RequiredAsBoolean_CustomLiterals_ThrowsOnUnparsable()
    {
        using EnvironmentScope scope = Scope();

        Assert.True(EnvVar.Set(NameA, "enabled").RequiredAsBoolean("enabled", "disabled"));
        Assert.Throws<InvalidOperationException>(() => EnvVar.Set(NameA, "1").RequiredAsBoolean("enabled", "disabled"));
    }

    // ==== 类型化解析：枚举 ====

    [Fact]
    public void AsEnum_ParsesName_IgnoringCaseByDefault()
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(NameA, "alpha");

        Assert.Equal(TestMode.Alpha, env.AsEnum<TestMode>());
        Assert.Equal(TestMode.Alpha, env.AsEnum<TestMode>(requireDefined: true));
    }

    [Fact]
    public void AsEnum_AcceptsNumericString_OnlyWhenRequireDefinedIsFalse()
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(NameA, "99");

        Assert.Equal((TestMode)99, env.AsEnum<TestMode>());
        Assert.Equal(TestMode.None, env.AsEnum<TestMode>(requireDefined: true));
        Assert.Equal(TestMode.Beta, env.AsEnum<TestMode>(TestMode.Beta, requireDefined: true));
    }

    [Fact]
    public void AsEnum_ReturnsDefault_ForUnparsable()
    {
        using EnvironmentScope scope = Scope();
        EnvVar env = EnvVar.Set(NameA, "nonsense");

        Assert.Equal(TestMode.None, env.AsEnum<TestMode>());
        Assert.Equal(TestMode.Beta, env.AsEnum<TestMode>(TestMode.Beta));
    }

    [Fact]
    public void RequiredAsEnum_Parses_AndRejectsUndefinedOrUnparsable()
    {
        using EnvironmentScope scope = Scope();

        Assert.Equal(TestMode.Beta, EnvVar.Set(NameA, "beta").RequiredAsEnum<TestMode>());

        EnvVar numeric = EnvVar.Set(NameA, "99");
        Assert.Equal((TestMode)99, numeric.RequiredAsEnum<TestMode>(requireDefined: false));

        InvalidOperationException undefined = Assert.Throws<InvalidOperationException>(() => numeric.RequiredAsEnum<TestMode>());
        Assert.Contains("is not a defined member", undefined.Message);

        InvalidOperationException unparsable =
            Assert.Throws<InvalidOperationException>(() => EnvVar.Set(NameA, "nonsense").RequiredAsEnum<TestMode>());
        Assert.Contains("cannot be converted to", unparsable.Message);
    }
}

/// <summary>
/// 测试用的枚举（默认值 <see cref="None"/> 便于断言"解析失败返回默认值"）。
/// </summary>
internal enum TestMode
{
    /// <summary>默认值。</summary>
    None = 0,

    /// <summary>测试取值之一。</summary>
    Alpha = 1,

    /// <summary>测试取值之一。</summary>
    Beta = 2,
}

/// <summary>
/// 测试用的自定义引用类型：<see cref="IParsable{TSelf}"/> 的 <c>TryParse</c> 在值以 <c>!</c> 开头时失败。
/// 用于验证 <see cref="EnvVar.As{T}"/> 对引用类型解析失败返回 <see langword="null"/>。
/// </summary>
internal sealed class UnparsableWidget : IParsable<UnparsableWidget>
{
    /// <summary>解析时传入的原始字符串。</summary>
    public string Raw { get; init; } = "";

    /// <inheritdoc/>
    public static UnparsableWidget Parse(string s, IFormatProvider? provider) => new() { Raw = s };

    /// <inheritdoc/>
    public static bool TryParse(string? s, IFormatProvider? provider, out UnparsableWidget result)
    {
        if (s is null || s.StartsWith('!'))
        {
            result = null!;
            return false;
        }

        result = new UnparsableWidget { Raw = s };
        return true;
    }
}
