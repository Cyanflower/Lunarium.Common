namespace Lunarium.Common;

/// <summary>
/// 常用字符集常量集合。
/// 提供数字、字母、各类 Base 编码字母表，以及防混淆与密码生成用的字符集，
/// 可直接用于随机字符串生成、编码转换、短 ID / 验证码 / 兑换码等场景。
/// </summary>
public static class Alphabet
{
    // ====== 基础字母与数字 ======

    /// <summary>十进制数字字符集：0-9。</summary>
    public const string Digits = "0123456789";

    /// <summary>小写英文字母字符集：a-z。</summary>
    public const string Lowercase = "abcdefghijklmnopqrstuvwxyz";

    /// <summary>大写英文字母字符集：A-Z。</summary>
    public const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>英文字母字符集（小写在前、大写在后）：a-zA-Z。</summary>
    public const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>通用字符集（数字 + 小写 + 大写），即 62 进制字母表，常用作随机字符池基。</summary>
    public const string Base = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // ====== 编码常用 (Base Ex系列) ======

    /// <summary>16 进制小写字母表：0-9、a-f。</summary>
    public const string HexLower = "0123456789abcdef";

    /// <summary>16 进制大写字母表：0-9、A-F。</summary>
    public const string HexUpper = "0123456789ABCDEF";

    /// <summary>Base32 字母表（RFC 4648），常用于 2FA / TOTP 密钥。</summary>
    public const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Base36 字母表（0-9、a-z），常用于大整数转紧凑短串或文件名。</summary>
    public const string Base36 = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>Base58 字母表，常用于区块链 / IPFS / 短网址；已剔除易混淆字符 0、O、I、l。</summary>
    public const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Base64 标准字母表（含 + / 符号）。</summary>
    public const string Base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    /// <summary>Base64 URL 安全字母表（含 - _ 符号），适用于 URL 与文件名。</summary>
    public const string UrlSafe = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-_";

    // ====== 防混淆 / 人类可读 (极其适合 验证码 / 兑换码 / 短ID) ======
    // 去除了容易肉眼看错的字符：0/O, 1/I/l, 2/Z, 8/B 等

    /// <summary>
    /// Crockford's Base32 字母表，常用于兑换码、短码；不区分大小写，并排除易混淆的 I、L、O、U。
    /// </summary>
    public const string CrockfordBase32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// 强防混淆字母数字字符集（兑换码 / 验证码首选）；已剔除 0、1、I、O 等易混淆字符。
    /// </summary>
    public const string HumanReadable = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>纯数字验证码友好字符集：去掉了容易混淆的 0 和 1。</summary>
    public const string DigitsNoAmbiguous = "23456789";

    // ====== 安全 / 密码生成 ======

    /// <summary>常用特殊符号字符集，用于密码生成、盐值等场景。</summary>
    public const string Special = "!@#$%^&*()_+-=[]{}|;:,.<>?";

    /// <summary>复杂密码全集：数字 + 大小写字母 + 特殊符号。</summary>
    public const string Complex = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!@#$%^&*()_+-=[]{}|;:,.<>?";
}