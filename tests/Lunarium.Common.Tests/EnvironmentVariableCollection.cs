namespace Lunarium.Common.Tests;

/// <summary>
/// 需要读写进程级环境变量的测试集合。
/// </summary>
/// <remarks>
/// 环境变量是进程级全局状态，而 xUnit 默认并行跑测试类；
/// 因此所有涉及环境变量的测试类都必须挂到本集合上（已禁用并行），避免用例之间互相干扰。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentVariableCollection
{
    /// <summary>集合名，供测试类的 <see cref="CollectionAttribute"/> 引用。</summary>
    public const string Name = "Environment variables";
}
