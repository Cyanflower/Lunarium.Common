namespace Lunarium.Common.Tests;

/// <summary>
/// 环境变量快照作用域：构造时记录指定变量的原值，<see cref="Dispose"/> 时逐一恢复。
/// </summary>
/// <remarks>
/// 配合 <see cref="EnvironmentVariableCollection"/>（禁用并行）使用，保证测试不会污染
/// 宿主机上真实存在的环境变量（例如 CI 里可能已经设置了 <c>http_proxy</c>）。
/// </remarks>
internal sealed class EnvironmentScope : IDisposable
{
    private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

    /// <summary>
    /// 记录 <paramref name="names"/> 的当前值。
    /// </summary>
    /// <param name="names">需要在作用域结束后恢复的环境变量名。</param>
    public EnvironmentScope(params string[] names)
    {
        foreach (string name in names)
        {
            _original[name] = Environment.GetEnvironmentVariable(name);
        }
    }

    /// <summary>
    /// 把作用域内的环境变量恢复到构造时的状态。
    /// </summary>
    public void Dispose()
    {
        foreach (KeyValuePair<string, string?> entry in _original)
        {
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
    }
}
