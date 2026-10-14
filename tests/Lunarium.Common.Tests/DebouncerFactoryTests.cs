using Lunarium.Common.Debounce;

namespace Lunarium.Common.Tests;

public class DebouncerFactoryTests
{
    [Fact]
    public void CreateDirty_NullAction_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => Debouncer.CreateDirty((Action)null!, TimeSpan.FromSeconds(1)));

    [Fact]
    public void CreateDirty_NullActionWithOptions_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => Debouncer.CreateDirty((Action)null!, TimeSpan.FromSeconds(1), new DirtyDebouncer.Options()));

    [Fact]
    public void CreateCts_NullAction_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => Debouncer.CreateCts((Action)null!, TimeSpan.FromSeconds(1)));

    [Fact]
    public async Task CreateCts_ReturnsStartedInstance()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateCts(() => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(20));

        debouncer.Invoke();

        Assert.True(await TestHelpers.WaitUntilAsync(() => Volatile.Read(ref runs) == 1, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task CreateDirty_ReturnsStartedInstance()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateDirty(() => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(20));

        debouncer.MarkDirty();

        Assert.True(await TestHelpers.WaitUntilAsync(() => Volatile.Read(ref runs) == 1, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task CreateDirty_SyncOverload_WrapsIntoAsyncAction()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateDirty(
            () => Interlocked.Increment(ref runs), TimeSpan.FromSeconds(30), new DirtyDebouncer.Options());

        await debouncer.ExecuteNowAsync();

        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task CreateCts_AsyncOverload_ReceivesCancellationToken()
    {
        bool tokenIsDefault = true;
        using var external = new CancellationTokenSource();
        var options = new CtsDebouncer.Options { ActionCancellationToken = external.Token };

        await using var debouncer = Debouncer.CreateCts(
            ct => { tokenIsDefault = ct == default; return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(20),
            options);

        debouncer.Invoke();
        await debouncer.ExecuteNowAsync();

        Assert.False(tokenIsDefault);       // 透传的是外部令牌而非 default
    }
}
