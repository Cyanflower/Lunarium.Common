# UID API

命名空间：`Lunarium.Common.UID`

两个独立的 ID 生成器：`NanoId` 生成随机字符串，`LocalSnowflake` 生成单机 64 位雪花 ID。

## NanoId

```csharp
public static class NanoId
{
    public static string Generate(
        int size = 21,
        string alphabet = Alphabet.UrlSafe);

    public static byte[] GenerateUtf8(
        int size = 21,
        string alphabet = Alphabet.UrlSafe);

    public static void GenerateUtf8(
        Span<byte> destination,
        string alphabet = Alphabet.UrlSafe);
}
```

| 成员 | 输出 | 说明 |
| --- | --- | --- |
| `Generate` | `string` | 默认 21 字符，默认字符集 `Alphabet.UrlSafe` |
| `GenerateUtf8(int, string)` | `byte[]` | 直接返回 UTF-8 字节，省去中间字符串 |
| `GenerateUtf8(Span<byte>, string)` | `void` | 长度由 `destination.Length` 决定；就地写入，零分配 |

语义：

- 使用加密随机源 `RandomNumberGenerator`，两条路径都没有取模偏差（modulo bias）。
- 字符集长度为 2 的幂（如 16/32/64/128/256）时走位掩码快速路径，否则走拒绝采样。
- `size` 必须大于 0；`alphabet` 长度必须为 1~256。
- 字符串重载允许非 ASCII 字符集；UTF-8 重载要求字符集仅含 ASCII（每个字符恰好 1 字节）。
- 字符集重复字符会改变各字符的实际出现概率；需要均匀分布时请使用去重字符集。
- 默认 `UrlSafe` 为 64 字符，21 字符约 126 bit 熵。

异常：

| 场景 | 异常 |
| --- | --- |
| `size <= 0` | `ArgumentOutOfRangeException` |
| `alphabet == null` | `ArgumentNullException` |
| `alphabet` 为空 | `ArgumentException` |
| `alphabet.Length > 256` | `ArgumentOutOfRangeException` |
| UTF-8 重载字符集含非 ASCII | `ArgumentException` |
| `destination` 为空 | `ArgumentOutOfRangeException` |

示例：

```csharp
using Lunarium.Common;
using Lunarium.Common.UID;

string token = NanoId.Generate();                          // 21 chars, UrlSafe
string code = NanoId.Generate(12, Alphabet.HumanReadable); // 12 chars, 防混淆
byte[] utf8 = NanoId.GenerateUtf8();

Span<byte> buffer = stackalloc byte[21];
NanoId.GenerateUtf8(buffer);                               // 写入调用方缓冲区
```

## LocalSnowflake

```csharp
public static class LocalSnowflake
{
    public static long NextId();

    public static DateTimeOffset ExtractTime(long id);

    public static bool IsExpired(long id, TimeSpan ttl);
}
```

位布局：

```text
[ 1 bit 符号(0) | 41 bits 毫秒时间戳(自 UTC 2026-01-01) | 22 bits 序列号 ]
```

| 成员 | 行为 |
| --- | --- |
| `NextId()` | 返回下一个恒为正的 64 位 ID；线程安全、进程内单调递增 |
| `ExtractTime(id)` | 解算 ID 的 UTC 生成时间；时间戳超出 `DateTimeOffset` 范围时抛 `ArgumentOutOfRangeException` |
| `IsExpired(id, ttl)` | 判断从生成时刻起是否已超过 `ttl`（严格大于） |

语义与限制：

- 无锁 CAS 实现；同一毫秒内序列号用尽时借用下一毫秒。
- 时钟回拨时借用逻辑时间，ID 不会倒退；极端情况下 `ExtractTime` 可能略超前于墙钟。
- **没有机器/节点编号**，只保证单进程内唯一。多进程/多机器使用可能重复，不能替代分布式 ID 方案。
- 41 位时间戳从 UTC 2026-01-01 起约可用 69 年；同一毫秒最多约 419 万个序列号。
- `ExtractTime` / `IsExpired` 依赖上述纪元与位布局；不要用于解析其他来源的雪花 ID。
- `ttl` 为负时，已生成的 ID 会被视为已过期。

示例：

```csharp
using Lunarium.Common.UID;

long id = LocalSnowflake.NextId();
DateTimeOffset createdAt = LocalSnowflake.ExtractTime(id);
bool expired = LocalSnowflake.IsExpired(id, TimeSpan.FromHours(24));
```

## 选型

| | `NanoId` | `LocalSnowflake` |
| --- | --- | --- |
| 输出 | 字符串 / UTF-8 字节 | 64 位 `long` |
| 排序 | 无 | 进程内单调递增 |
| 时间信息 | 无 | 可解算生成时间 |
| 多进程 | 随机，无需协调；无硬唯一保证 | 单进程；无 node id，多进程可能重复 |
| 适合 | 请求 ID、token、短码、验证码 | 单机事件 ID、按时间排序、TTL 判断 |

更完整的说明见[使用指南](../README.md)。

## 线程安全 / AOT

- `NanoId`：加密随机源可并发调用；无反射、无动态代码。
- `LocalSnowflake`：`Interlocked.CompareExchange` 无锁 CAS，线程安全；无反射。

可运行示例：[IdExample.cs](../examples/IdExample.cs)。
