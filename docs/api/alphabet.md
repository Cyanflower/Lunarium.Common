# Alphabet API

命名空间：`Lunarium.Common` ｜ 类型：`public static class Alphabet`

常用字符集常量集合。所有成员都是 `public const string`，可直接用于随机字符串、短 ID、验证码、兑换码、编码转换或密码生成。

## 常量

| 常量 | 值 | 说明 |
| --- | --- | --- |
| `Digits` | `0123456789` | 十进制数字 |
| `Lowercase` | `abcdefghijklmnopqrstuvwxyz` | 小写字母 |
| `Uppercase` | `ABCDEFGHIJKLMNOPQRSTUVWXYZ` | 大写字母 |
| `Letters` | `abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ` | 字母，小写在前 |
| `Base` | `0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ` | 数字 + 小写 + 大写（62 字符） |
| `HexLower` | `0123456789abcdef` | 小写十六进制 |
| `HexUpper` | `0123456789ABCDEF` | 大写十六进制 |
| `Base32` | `ABCDEFGHIJKLMNOPQRSTUVWXYZ234567` | RFC 4648 Base32，常用于 2FA/TOTP |
| `Base36` | `0123456789abcdefghijklmnopqrstuvwxyz` | 0-9 + a-z |
| `Base58` | `123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz` | 已剔除 `0`、`O`、`I`、`l` |
| `Base64` | `ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/` | 标准 Base64，含 `+` `/` |
| `UrlSafe` | `0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-_` | URL 安全 Base64 变体，含 `-` `_` |
| `CrockfordBase32` | `0123456789ABCDEFGHJKMNPQRSTVWXYZ` | 排除 `I`、`L`、`O`、`U`；常用于兑换码/短码 |
| `HumanReadable` | `23456789ABCDEFGHJKLMNPQRSTUVWXYZ` | 强防混淆；排除 `0`、`1`、`I`、`O` |
| `DigitsNoAmbiguous` | `23456789` | 纯数字验证码友好；排除 `0`、`1` |
| `Special` | 见下方代码块 | 常用特殊符号 |
| `Complex` | 见下方代码块 | 数字 + 大小写字母 + 特殊符号 |

含 `|` 的两个常量单独列出，避免 Markdown 表格把竖线当成列分隔符：

```text
Special = !@#$%^&*()_+-=[]{}|;:,.<>?
Complex = 0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!@#$%^&*()_+-=[]{}|;:,.<>?
```

## 语义

- 常量在编译期内联，不产生运行时分配；`Alphabet` 本身没有实例状态。
- `NanoId.Generate` 的默认字符集是 `UrlSafe`；`HumanReadable`、`CrockfordBase32`、`Base32` 长度均为 32（2 的幂），会走 `NanoId` 的快速路径。
- `Special` / `Complex` 包含 URL 保留字符或 shell 特殊字符；用于 URL、文件名、命令行参数前请自行转义。
- `Base58`、`CrockfordBase32`、`HumanReadable`、`DigitsNoAmbiguous` 的取舍是“减少肉眼混淆”，并不等于加密安全。

## 示例

```csharp
using Lunarium.Common;
using Lunarium.Common.UID;

string humanCode = NanoId.Generate(12, Alphabet.HumanReadable);
string crockford = NanoId.Generate(16, Alphabet.CrockfordBase32);
string hex = NanoId.Generate(8, Alphabet.HexLower);
string password = NanoId.Generate(24, Alphabet.Complex);
```

自定义字符集仍受 `NanoId` 的约束：长度 1~256；`GenerateUtf8` 重载要求字符集仅含 ASCII。字符集不必去重，但重复字符会改变各字符的实际出现概率。

## 线程安全 / AOT

- 全部是 `const string`，不可变，可安全并发读取。
- 无反射、无动态代码，AOT / trimming 安全。

可运行示例：[IdExample.cs](../examples/IdExample.cs)（使用 `Alphabet.HumanReadable`）。
