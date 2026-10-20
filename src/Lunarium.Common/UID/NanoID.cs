using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Lunarium.Common.UID;

/// <summary>
/// 支持自定义字符集与长度的高性能 NanoId 生成器
/// </summary>
/// <remarks>
/// 默认生成 21 个字符的 URL 安全 ID, 字符集长度为 2 的幂时走位掩码快路径,
/// 否则退化为拒绝采样路径; 两条路径均无取模偏差 (modulo bias)。
/// </remarks>
public static class NanoId
{
    private const int DefaultSize = 21;

    /// <summary>
    /// 生成一个随机 NanoId。
    /// </summary>
    /// <param name="size">生成的 Id 长度, 必须大于 0; 默认 21</param>
    /// <param name="alphabet">自定义字符集, 长度需为 1~256; 默认 <see cref="Alphabet.UrlSafe"/></param>
    /// <returns>由 <paramref name="alphabet"/> 中字符随机组成、长度为 <paramref name="size"/> 的字符串</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> 小于等于 0, 或 <paramref name="alphabet"/> 长度超过 256</exception>
    /// <exception cref="ArgumentNullException"><paramref name="alphabet"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="alphabet"/> 为空字符串。</exception>
    public static string Generate(int size = DefaultSize, string alphabet = Alphabet.UrlSafe)
    {
        ValidateArguments(size, alphabet);

        // 如果字符集长度刚好是 2 的幂(64, 32, 16), 可以使用快速位掩码路径
        if (BitOperations.IsPow2(alphabet.Length))
        {
            return GenerateFastPow2(size, alphabet);
        }

        // 通用无偏算法(消除 Modulo Bias)
        return GenerateUnbiased(size, alphabet);
    }

    /// <summary>
    /// 生成一个随机 NanoId, 并直接返回 UTF-8 字节序列。
    /// </summary>
    /// <remarks>
    /// 结果与 <c>Encoding.UTF8.GetBytes(Generate(size, alphabet))</c> 相同, 但省去中间字符串分配与转码,
    /// 适合把 ID 直接写入 JSON / 网络缓冲区 / 哈希计算的场景。由于要求每个字符只占 1 字节,
    /// <paramref name="alphabet"/> 必须仅包含 ASCII 字符; 非 ASCII 字符集请使用
    /// <see cref="Generate(int, string)"/> 再自行编码。
    /// </remarks>
    /// <param name="size">生成的 Id 长度, 必须大于 0; 默认 21</param>
    /// <param name="alphabet">自定义字符集, 长度需为 1~256 且仅包含 ASCII 字符; 默认 <see cref="Alphabet.UrlSafe"/></param>
    /// <returns>长度为 <paramref name="size"/> 的 UTF-8 字节数组</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> 小于等于 0, 或 <paramref name="alphabet"/> 长度超过 256</exception>
    /// <exception cref="ArgumentNullException"><paramref name="alphabet"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="alphabet"/> 为空, 或包含非 ASCII 字符。</exception>
    public static byte[] GenerateUtf8(int size = DefaultSize, string alphabet = Alphabet.UrlSafe)
    {
        ValidateArguments(size, alphabet);
        ValidateAsciiAlphabet(alphabet);

        byte[] result = new byte[size];
        FillUtf8(result, alphabet);
        return result;
    }

    /// <summary>
    /// 生成一个随机 NanoId, 并直接写入调用方提供的 UTF-8 缓冲区 (全程零分配)。
    /// </summary>
    /// <remarks>
    /// <paramref name="destination"/> 的长度即 Id 的长度; 写入的字节与
    /// <see cref="GenerateUtf8(int, string)"/> 相同 (仅含 ASCII 字符集时每字符 1 字节)。
    /// 适合配合 <c>stackalloc</c> 或 <c>ArrayPool&lt;byte&gt;</c> 复用缓冲区, 做到生成过程零分配。
    /// </remarks>
    /// <param name="destination">目标缓冲区, 长度即 Id 长度, 必须大于 0</param>
    /// <param name="alphabet">自定义字符集, 长度需为 1~256 且仅包含 ASCII 字符; 默认 <see cref="Alphabet.UrlSafe"/></param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> 为空, 或 <paramref name="alphabet"/> 长度超过 256</exception>
    /// <exception cref="ArgumentNullException"><paramref name="alphabet"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="alphabet"/> 为空, 或包含非 ASCII 字符。</exception>
    public static void GenerateUtf8(Span<byte> destination, string alphabet = Alphabet.UrlSafe)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(destination.Length);
        ValidateAlphabet(alphabet);
        ValidateAsciiAlphabet(alphabet);

        FillUtf8(destination, alphabet);
    }

    /// <summary>
    /// 生成 NanoID 的快速路径: 适用于字符集长度为 2 的幂 (如 16/32/64/128/256) 的情形。
    /// </summary>
    /// <remarks>
    /// 每个随机字节与掩码按位与后直接得到字符索引, 低位均匀映射, 无需拒绝采样。
    /// </remarks>
    /// <param name="size">生成的 ID 长度, 需大于 0。</param>
    /// <param name="alphabet">字符集, 长度须为 2 的幂且不超过 256。</param>
    /// <returns>生成的 NanoID。</returns>
    private static string GenerateFastPow2(int size, string alphabet)
    {
        return string.Create(size, alphabet, static (chars, alpha) =>
        {
            // 创建对应位宽掩码
            int mask = alpha.Length - 1;

            // 掩码位宽: 位宽为"表示字符集最大下标所需的二进制位数"
            // 以字符集为 64 为例, 64 的二进制为 0100 0000
            // 包含全0在内, 二进制在 0000 0000(0) ~ 0011 1111(63) 即可满足 64 种组合
            // 0000 0000(0) ~ 0100 0000 (64)反而会多1(65种组合), 随机数真roll到64会数组越界, 64长度的数组的索引范围是0~63

            // 栈分配 byte 数组: 小于 256 字节直接在线程栈上分配, 不触发 GC 回收
            Span<byte> randomBytes = chars.Length <= 256 ? stackalloc byte[chars.Length] : new byte[chars.Length];

            // 对数组每个元素灌入随机数, 随机数值的范围为 0~255 (数组的元素是 byte)
            // Before: randomBytes[0x00, 0x00, 0x00, 0x00]
            RandomNumberGenerator.Fill(randomBytes);
            // After: randomBytes[182, 45, 9, 213]

            // 循环: 将随机数列表的每一个随机数, 转为字符表中对应的字符
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = alpha[randomBytes[i] & mask];
                // 按照顺序索引, 取随机数, 通过掩码将随机数转译为字符表的索引顺序, 并将索引所在的字符填入结果
                // & mask 用于保证在字符集长度远比byte范围小的情况下, 保证随机数不越界(截断高位)
                // 例如随机数抽取到了 150 (1001 0110), 长度为64的字符集的数组索引只有0~63, 访问第150个元素会直接越界
                // 位运算: &
                // 旧数值: 1001 0110 (150)
                // 掩码值: 0011 1111 (63) 
                //         ↓ 用0清除, 用1掩码 
                // 新数值: 0001 0110 (22) 截断高位
                // 将字符集数组中索引为22(第23个元素)加入到结果中
            }
        });
    }

    /// <summary>
    /// 生成 NanoID 的通用路径: 适用于任意长度字符集 (如 62/36/10), 采用拒绝采样消除取模偏差。
    /// </summary>
    /// <param name="size">生成的 ID 长度, 需大于 0。</param>
    /// <param name="alphabet">字符集, 长度任意 (1~256)。</param>
    /// <returns>生成的 NanoID。</returns>
    private static string GenerateUnbiased(int size, string alphabet)
    {
        // 对于任意长度的字符集, 动态计算出一个位宽最小能覆盖 字符集二进制表示位宽 的掩码位宽
        // 非最小会导致拒绝采样的拒绝率上升, 进而导致采样次数增加(见采样部分)
        int mask = (int)BitOperations.RoundUpToPowerOf2((uint)alphabet.Length) - 1;
        // 例如字符集的长度为 58 (0011 1010)
        // 最小覆盖的掩码应为 63 (0011 1111), 小了会导致低位覆盖不全


        // 随机步数: 控制随机字节数量, 因为拒绝采样的拒绝率的存在, 需要预估抽取更多的随机数来尽可能保证有足够的随机数通过采样, 才能满足对应数量的分配需求
        int step = (int)Math.Ceiling(1.6 * mask * size / alphabet.Length);

        return string.Create(size, (alphabet, mask, step), static (chars, state) =>
        {
            // 0 GC 分配打包元组参数
            var (alpha, bitMask, bufferSize) = state;

            // 栈分配 byte 数组: 小于 256 字节直接在线程栈上分配, 不触发 GC 回收
            // 此处数组长度是 step(bufferSize)
            Span<byte> randomBytes = bufferSize <= 256
                ? stackalloc byte[bufferSize]
                : new byte[bufferSize];

            // 第 x 个字符
            int charIndex = 0;

            // 拒绝采样兜底循环, 如果结果长度够了, 直接结束循环
            while (charIndex < chars.Length)
            {
                // 对step(bufferSize)数组每个元素灌入随机数
                RandomNumberGenerator.Fill(randomBytes);

                // 循环: 将随机数列表的每一个随机数, 转为字符表中对应的字符, 如果结果长度够了, 直接结束循环
                for (int i = 0; i < randomBytes.Length && charIndex < chars.Length; i++)
                {
                    // 按照顺序索引, 取随机数, 通过掩码将随机数转译为字符表的索引顺序
                    int alphabetIndex = randomBytes[i] & bitMask;
                    // 此处随机数可能大于数组索引
                    // 原因为方法开头的掩码为: 位宽最小能覆盖 字符集二进制表示位宽 的掩码位宽
                    // 最小覆盖但不等于:
                    // > 例如字符集的长度为 58 (0011 1010)
                    // > 最小覆盖的掩码应为 63 (0011 1111), 小了会导致低位覆盖不全
                    // 例如字符集数组长度为58, 索引范围0~57, 但最小掩码是63
                    // 大于63的随机数经过掩码的高位裁剪后, 仍然可能为大于数组索引的数, 如58~63
                    // 直接使用58~63访问字符集会直接导致越界

                    // 进行拒绝采样
                    // 如果最终随机数在数组索引内, 将索引所在的字符填入结果
                    if (alphabetIndex < alpha.Length)
                    {
                        chars[charIndex++] = alpha[alphabetIndex];
                    }
                    // 否则拒绝, for循环继续下一轮, i++, 选取下一个随机数, 再次尝试该随机数最终是否落在字符集数组索引区间内
                    // 以字符集为58为例, 如果直接设为过大的掩码值将直接导致, 掩码转译后的随机数结果范围过大, 如 127 (0111 1111)
                    // 导致最终随机数范围足足有0~127, 能落在 0~57 区间的随机数将不足50%
                    // 随机数数组将完全不足以支撑分配到对应 size 长度
                    // 不到50%通过率必然导致多轮while的随机数重新生成
                }
                // 如果step(bufferSize)数组最后一个随机数用完后, 因为之前拒绝过多, 结果长度仍然不满足size
                // 将通过while循环重新进行一次随机数数组生成, 并继续采样并填充剩余部分
            }
        });
    }

    /// <summary>
    /// 校验长度与字符集的公共参数约束。
    /// </summary>
    private static void ValidateArguments(int size, string alphabet)
    {
        // 长度不能为负或为0
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ValidateAlphabet(alphabet);
    }

    /// <summary>
    /// 校验字符集非空且长度不超过 256。
    /// </summary>
    private static void ValidateAlphabet(string alphabet)
    {
        // 字符集不能是空的
        ArgumentException.ThrowIfNullOrEmpty(alphabet);

        if (alphabet.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(alphabet), "Alphabet 长度不能超过 256 个字符。");
        }
    }

    /// <summary>
    /// UTF-8 字节输出要求每个字符恰好编码为 1 字节, 因此字符集必须全部是 ASCII。
    /// </summary>
    private static void ValidateAsciiAlphabet(string alphabet)
    {
        if (!Ascii.IsValid(alphabet))
        {
            throw new ArgumentException("UTF-8 字节输出要求字符集仅包含 ASCII 字符。", nameof(alphabet));
        }
    }

    /// <summary>
    /// 将随机 NanoID 直接以 UTF-8 字节写入目标缓冲区 (零分配)。
    /// </summary>
    private static void FillUtf8(Span<byte> destination, string alphabet)
    {
        if (BitOperations.IsPow2(alphabet.Length))
        {
            FillUtf8FastPow2(destination, alphabet);
        }
        else
        {
            FillUtf8Unbiased(destination, alphabet);
        }
    }

    /// <summary>
    /// UTF-8 快速路径: 字符集长度为 2 的幂, 直接在目标缓冲区上就地随机填充并掩码映射。
    /// </summary>
    private static void FillUtf8FastPow2(Span<byte> destination, string alphabet)
    {
        int mask = alphabet.Length - 1;

        // 直接把随机字节写进目标缓冲区, 省掉独立的随机数缓冲区
        RandomNumberGenerator.Fill(destination);

        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)alphabet[destination[i] & mask];
        }
    }

    /// <summary>
    /// UTF-8 通用路径: 拒绝采样消除取模偏差, 通过原地压缩避免额外的随机数缓冲区。
    /// </summary>
    private static void FillUtf8Unbiased(Span<byte> destination, string alphabet)
    {
        int mask = (int)BitOperations.RoundUpToPowerOf2((uint)alphabet.Length) - 1;
        int written = 0;

        while (written < destination.Length)
        {
            // 只对尚未填充的尾部生成随机字节; 已写出的前缀不会再被触碰
            Span<byte> tail = destination[written..];
            RandomNumberGenerator.Fill(tail);

            int accepted = 0;
            for (int i = 0; i < tail.Length; i++)
            {
                int alphabetIndex = tail[i] & mask;
                if (alphabetIndex < alphabet.Length)
                {
                    // accepted <= i, 因此写回不会覆盖尚未读取的随机字节
                    tail[accepted++] = (byte)alphabet[alphabetIndex];
                }
            }

            written += accepted;
        }
    }
}