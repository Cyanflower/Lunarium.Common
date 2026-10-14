using System.Runtime.CompilerServices;

namespace Lunarium.Common.UID;

/// <summary>
/// 极简无锁单机雪花 ID 生成器 (1 bit 符号 + 41 bits 毫秒时间戳 + 22 bits 序列号)
/// </summary>
/// <remarks>
/// 位布局: [ 1 bit 符号(恒为 0) | 41 bits 毫秒时间戳(自 UTC 2026-01-01 起) | 22 bits 序列号 ]
/// 线程安全且单调递增; 发生时钟回拨时借用逻辑时间, 保证 ID 不会倒退
/// </remarks>
public static class LocalSnowflake
{
    // 自定义纪元基准时间
    // 41位时间戳可连续使用约 69 年
    // UTC 2026-1-1 0:0:0
    private const long Epoch = 1767225600000;
    private const int SequenceBits = 22;
    // 22 位2进制全 1 掩码最大值
    private const long MaxSequence = (1L << SequenceBits) - 1; // 4,194,303
    private static long _lastState = -1L;

    /// <summary>
    /// 生成下一个全局唯一, 单向递增的 64 位 ID。
    /// </summary>
    /// <returns>新生成的 64 位雪花 ID (恒为正数)。</returns>
    public static long NextId()
    {
        while (true)
        {
            // 强制读取内存中最新的 _lastState (上一条id)
            long oldState = Volatile.Read(ref _lastState);

            // 将旧状态右移 22 位, 抹掉低位的序列号, 取出时间戳部分
            long lastTimestamp = oldState >> SequenceBits;

            // 计算当前时间和 Epoch 的时间差
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Epoch;

            // 防止时钟回拨/抖动, 直接借用未来时间, 强制取较大值, 保证逻辑时间只进不退(单向前进)
            long currentTimestamp = Math.Max(now, lastTimestamp);

            long sequence;

            // 还在同一毫秒内
            if (currentTimestamp == lastTimestamp)
            {
                // 位运算: & (与)
                // 低 22 位是序列号, 直接 +1 即可完成序列号自增
                // & MaxSequence(掩码)：抹除高位时间戳，且满溢时自动绕回 0
                sequence = (oldState + 1) & MaxSequence;


                if (sequence == 0)
                {
                    // 如果同一个毫秒内, 用光了 22 位序列号 (419万个)
                    // 直接借用下一毫秒
                    currentTimestamp++;
                }

                // sequence 自增:
                // 位布局: [ 1 bit 符号(恒为 0) | 41 bits 毫秒时间戳(自 UTC 2026-01-01 起) | 22 bits 序列号 ]
                // 旧值: 0|000 0100 0101 1011 1101 0110 0111 1111 0101 1010 01|00 0101 1010 0111 1011 1001
                // 新值: 0|000 0100 0101 1011 1101 0110 0111 1111 0101 1010 01|00 0101 1010 0111 1011 [1010] <- +1对末位加1, 只序列号段变化, 不污染时间戳

                // 位运算: & (与)
                // operator &(left, right), 左为当前值, 右为掩码值
                // e.g:
                // 当前值: 0010 1101 1011
                // 掩码值: 0000 0011 1111 ↓ 用0清除, 用1掩码
                // 结果值: 0000 0001 1011
                // 绕0例子:
                // 旧数值: 0000 0011 1111
                // +1新值: 0000 0100 0000
                // 掩码值: 0000 0011 1111
                // 结果值: 0000 0000 0000
            }
            else
            {
                // 新的毫秒, 序列号归0
                sequence = 0;
            }

            // 位运算: 位移 并 按位或运算
            // 布局: [ currentTimestamp (41位) | sequence (22位) ]
            // 将时间戳左移 22 位, 然后和序列号做按位或, 拼出新的 64 位 id
            long newState = (currentTimestamp << SequenceBits) | sequence;
            // 位运算: << (左位移)
            // 旧数值: 0000 0010 1011
            // << 4
            // 新数值: 0010 1011 0000
            // 位运算: | (按位或)
            // 左侧值: 0010 1011 0000
            // 右侧值: 0000 0000 0101 ↓ 0 | x => x
            // 结果值: 0010 1011 0101

            // CAS 原子替换
            // 如果当前内存中的 _lastState 依然等于在 Volatile.Read(ref _lastState) 读到的 oldState
            // 说明方法执行期间没有其他线程横插一脚, 原子地将其更新为 newState 并直接返回
            if (Interlocked.CompareExchange(ref _lastState, newState, oldState) == oldState)
            {
                return newState;
            }

            // 如果 CompareExchange 返回的值 != oldState，说明有并发冲突(其他线程的方法抢先一步)
            // while(true) 不退出，原地自旋立即重试下一轮，直到成功
        }
    }

    /// <summary>
    /// 直接从 ID 中解算创建时间
    /// </summary>
    /// <param name="id">由 <see cref="NextId"/> 生成的雪花 ID</param>
    /// <returns>该 ID 生成时的 UTC 时间</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 解算出的毫秒时间戳超出 <see cref="DateTimeOffset"/> 的可表示范围。
    /// </exception>
    public static DateTimeOffset ExtractTime(long id)
    {
        long timestamp = (id >> SequenceBits) + Epoch;
        return DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
    }

    /// <summary>
    /// 判断该 ID 对应的数据是否已超过指定的 TTL。
    /// </summary>
    /// <param name="id">由 <see cref="NextId"/> 生成的雪花 ID</param>
    /// <param name="ttl">存活时长; 自 ID 生成时刻起算, 超过该时长即视为过期</param>
    /// <returns>已过期返回 <see langword="true"/>; 否则返回 <see langword="false"/></returns>
    public static bool IsExpired(long id, TimeSpan ttl)
    {
        long createdMs = (id >> SequenceBits) + Epoch;
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return (nowMs - createdMs) > (long)ttl.TotalMilliseconds;
    }
}