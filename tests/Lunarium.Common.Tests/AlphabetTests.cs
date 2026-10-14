using System.Reflection;

namespace Lunarium.Common.Tests;

public class AlphabetTests
{
    [Fact]
    public void KnownAlphabets_HaveExpectedLengths()
    {
        Assert.Equal(10, Alphabet.Digits.Length);
        Assert.Equal(26, Alphabet.Lowercase.Length);
        Assert.Equal(26, Alphabet.Uppercase.Length);
        Assert.Equal(52, Alphabet.Letters.Length);
        Assert.Equal(62, Alphabet.Base.Length);
        Assert.Equal(16, Alphabet.HexLower.Length);
        Assert.Equal(16, Alphabet.HexUpper.Length);
        Assert.Equal(32, Alphabet.Base32.Length);
        Assert.Equal(36, Alphabet.Base36.Length);
        Assert.Equal(58, Alphabet.Base58.Length);
        Assert.Equal(64, Alphabet.Base64.Length);
        Assert.Equal(64, Alphabet.UrlSafe.Length);
        Assert.Equal(32, Alphabet.CrockfordBase32.Length);
    }

    [Fact]
    public void SpecDefinedAlphabets_MatchSpec()
    {
        // RFC 4648 Base32
        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567", Alphabet.Base32);
        // Crockford's Base32（排除易混淆的 I、L、O、U）
        Assert.Equal("0123456789ABCDEFGHJKMNPQRSTVWXYZ", Alphabet.CrockfordBase32);
        Assert.Equal("0123456789abcdef", Alphabet.HexLower);
        Assert.Equal("0123456789ABCDEF", Alphabet.HexUpper);
    }

    [Fact]
    public void AllAlphabets_AreNonEmptyAndHaveNoDuplicateCharacters()
    {
        var constants = typeof(Alphabet)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Value: (string)f.GetRawConstantValue()!))
            .ToList();

        Assert.NotEmpty(constants);
        foreach (var (name, value) in constants)
        {
            Assert.False(string.IsNullOrEmpty(value), $"{name} 不应为空");
            Assert.True(value.Distinct().Count() == value.Length, $"{name} 含重复字符: {value}");
        }
    }

    [Theory]
    [InlineData(nameof(Alphabet.HumanReadable))]
    [InlineData(nameof(Alphabet.DigitsNoAmbiguous))]
    public void AmbiguityFreeAlphabets_ExcludeConfusableDigitsAndLetters(string fieldName)
    {
        string alphabet = GetConstant(fieldName);
        foreach (char confusable in "01OI")
            Assert.True(!alphabet.Contains(confusable), $"{fieldName} 不应包含易混淆字符 '{confusable}'");
    }

    [Fact]
    public void CrockfordBase32_ExcludesConfusableLetters()
    {
        foreach (char confusable in "ILOU")
            Assert.True(!Alphabet.CrockfordBase32.Contains(confusable), $"CrockfordBase32 不应包含 '{confusable}'");
    }

    [Fact]
    public void Base58_ExcludesConfusableCharacters()
    {
        foreach (char confusable in "0OIl")
            Assert.True(!Alphabet.Base58.Contains(confusable), $"Base58 不应包含 '{confusable}'");
    }

    [Fact]
    public void Complex_IsUnionOfBaseAndSpecial()
    {
        Assert.Equal(Alphabet.Base.Length + Alphabet.Special.Length, Alphabet.Complex.Length);
        foreach (char c in Alphabet.Base)
            Assert.True(Alphabet.Complex.Contains(c), $"Complex 缺少 Base 中的 '{c}'");
    }

    private static string GetConstant(string fieldName) =>
        (string)typeof(Alphabet).GetField(fieldName, BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
}
