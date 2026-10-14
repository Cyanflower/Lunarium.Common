using Lunarium.Common.UID;

namespace Lunarium.Common.Tests;

public class NanoIDTests
{
    [Fact]
    public void Generate_DefaultSize_Is21UrlSafeCharacters()
    {
        for (int i = 0; i < 200; i++)
        {
            string id = NanoId.Generate();
            Assert.Equal(21, id.Length);
            foreach (char c in id)
                Assert.True(Alphabet.UrlSafe.Contains(c), $"ID 含字符集之外的字符 '{c}': {id}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(64)]
    [InlineData(257)]
    public void Generate_RespectsRequestedSize(int size)
        => Assert.Equal(size, NanoId.Generate(size).Length);

    [Theory]
    [InlineData(nameof(Alphabet.Base64))]   // 64: 2 的幂 → 位掩码快路径
    [InlineData(nameof(Alphabet.Base32))]   // 32: 2 的幂 → 位掩码快路径
    [InlineData(nameof(Alphabet.Base))]     // 62: 非 2 的幂 → 拒绝采样路径
    [InlineData(nameof(Alphabet.Base58))]   // 58: 非 2 的幂 → 拒绝采样路径
    [InlineData(nameof(Alphabet.Digits))]   // 10: 非 2 的幂 → 拒绝采样路径
    [InlineData(nameof(Alphabet.HumanReadable))]
    public void Generate_OnlyUsesCharactersFromAlphabet(string fieldName)
    {
        string alphabet = GetAlphabet(fieldName);
        for (int i = 0; i < 200; i++)
        {
            string id = NanoId.Generate(32, alphabet);
            foreach (char c in id)
                Assert.True(alphabet.Contains(c), $"字符 '{c}' 不在 {fieldName} 中");
        }
    }

    [Fact]
    public void Generate_SingleCharacterAlphabet_AlwaysReturnsThatCharacter()
    {
        for (int i = 0; i < 50; i++)
            Assert.Equal("xxxxx", NanoId.Generate(5, "x"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Generate_NonPositiveSize_Throws(int size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => NanoId.Generate(size));

    [Fact]
    public void Generate_NullAlphabet_Throws()
        => Assert.Throws<ArgumentNullException>(() => NanoId.Generate(5, null!));

    [Fact]
    public void Generate_EmptyAlphabet_Throws()
        => Assert.Throws<ArgumentException>(() => NanoId.Generate(5, string.Empty));

    [Fact]
    public void Generate_AlphabetLongerThan256_Throws()
    {
        string tooLong = new string(Enumerable.Repeat('a', 257).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => NanoId.Generate(5, tooLong));
    }

    [Fact]
    public void Generate_ProducesUniqueValues()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 20_000; i++)
            Assert.True(seen.Add(NanoId.Generate()), "生成出现重复值");
    }

    /// <summary>
    /// 分布检验：若实现存在取模偏差（modulo bias），低位字符会显著偏多，卡方值会远超阈值。
    /// 阈值取「自由度 + 6 倍标准差」量级，偶发超出的概率可忽略。
    /// </summary>
    [Theory]
    [InlineData(nameof(Alphabet.Base32))]   // 位掩码快路径
    [InlineData(nameof(Alphabet.Base))]     // 拒绝采样路径
    [InlineData(nameof(Alphabet.Digits))]   // 拒绝采样路径（字符集远小于 256，拒绝率最高）
    public void Generate_CharacterDistributionIsUniform(string fieldName)
    {
        string alphabet = GetAlphabet(fieldName);
        const int samplesPerChar = 4_000;
        int samples = alphabet.Length * samplesPerChar;
        var counts = new int[alphabet.Length];

        for (int i = 0; i < samples; i++)
            counts[alphabet.IndexOf(NanoId.Generate(1, alphabet)[0])]++;

        for (int i = 0; i < counts.Length; i++)
            Assert.True(counts[i] > 0, $"字符 '{alphabet[i]}' 在 {samples} 次采样中从未出现");

        double expected = (double)samples / alphabet.Length;
        double chiSquare = counts.Sum(c => (c - expected) * (c - expected) / expected);
        double threshold = (alphabet.Length - 1) + 6 * Math.Sqrt(2.0 * (alphabet.Length - 1));

        Assert.True(chiSquare < threshold, $"{fieldName} 卡方值 {chiSquare:F1} 超过阈值 {threshold:F1}，疑似存在取模偏差");
    }

    private static string GetAlphabet(string fieldName) =>
        (string)typeof(Alphabet).GetField(fieldName)!.GetRawConstantValue()!;
}
