using System;
using System.Numerics;
using System.Security.Cryptography;

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
        // 长度不能为负或为0
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        // 字符集不能是空的
        ArgumentException.ThrowIfNullOrEmpty(alphabet);

        if (alphabet.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(alphabet), "Alphabet 长度不能超过 256 个字符。");
        }

        // 如果字符集长度刚好是 2 的幂(64, 32, 16), 可以使用快速位掩码路径
        if (BitOperations.IsPow2(alphabet.Length))
        {
            return GenerateFastPow2(size, alphabet);
        }

        // 通用无偏算法(消除 Modulo Bias)
        return GenerateUnbiased(size, alphabet);
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
}