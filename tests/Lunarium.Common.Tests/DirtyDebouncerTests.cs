using System.Collections.Concurrent;
using Lunarium.Common.Debounce;

namespace Lunarium.Common.Tests;

public class DirtyDebouncerTests
{
    [Fact]
    public async Task MarkDirty_TriggersActionOncePerInterval()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateDirty(
            () => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(40));

        for (int i = 0; i < 5; i++) debouncer.MarkDirty();

        Assert.True(await TestHelpers.WaitUntilAsync(() => Volatile.Read(ref runs) >= 1, TimeSpan.FromSeconds(3)));
        await Task.Delay(150);
        Assert.Equal(1, Volatile.Read(ref runs));               // 脏标记已清除，不再重复执行
    }

    [Fact]
    public async Task IsDirty_ReflectsPendingState()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateDirty(
            () => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(40));

        Assert.False(debouncer.IsDirty);
        debouncer.MarkDirty();
        Assert.True(debouncer.IsDirty);

        Assert.True(await TestHelpers.WaitUntilAsync(() => !debouncer.IsDirty, TimeSpan.FromSeconds(3)));
        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task ExecuteNowAsync_RunsImmediatelyWithoutWaitingInterval()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateDirty(
            () => Interlocked.Increment(ref runs), TimeSpan.FromSeconds(30));

        debouncer.MarkDirty();
        await debouncer.ExecuteNowAsync();

        Assert.Equal(1, Volatile.Read(ref runs));
        Assert.False(debouncer.IsDirty);
    }

    [Fact]
    public async Task ExecuteNowAsync_OnFailure_KeepsDirtyForRetry()
    {
        int attempts = 0;
        var errors = new ConcurrentQueue<Exception>();
        await using var debouncer = new DirtyDebouncer(
            _ => { Interlocked.Increment(ref attempts); throw new InvalidOperationException("落盘失败"); },
            TimeSpan.FromSeconds(30),
            new DirtyDebouncer.Options { OnError = errors.Enqueue });

        debouncer.MarkDirty();
        await debouncer.ExecuteNowAsync();

        Assert.Equal(1, Volatile.Read(ref attempts));
        Assert.True(debouncer.IsDirty);                         // 失败 → 保留脏标记待重试
        Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(errors.TryPeek(out var ex) ? ex : null);
    }

    [Fact]
    public async Task Loop_RetriesAfterFailure()
    {
        int attempts = 0;
        var errors = new ConcurrentQueue<Exception>();
        await using var debouncer = new DirtyDebouncer(
            _ => { Interlocked.Increment(ref attempts); throw new InvalidOperationException("落盘失败"); },
            TimeSpan.FromMilliseconds(30),
            new DirtyDebouncer.Options { OnError = errors.Enqueue });

        debouncer.MarkDirty();

        Assert.True(await TestHelpers.WaitUntilAsync(() => attempts >= 3, TimeSpan.FromSeconds(5)));
        Assert.True(debouncer.IsDirty);
        Assert.All(errors, e => Assert.IsType<InvalidOperationException>(e));
    }

    [Fact]
    public async Task DisposeAsync_FlushesPendingDirtyDataBeforeReturning()
    {
        int runs = 0;
        var debouncer = Debouncer.CreateDirty(
            () => Interlocked.Increment(ref runs), TimeSpan.FromSeconds(30));

        debouncer.MarkDirty();
        await debouncer.DisposeAsync();

        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task MarkDirtyAndExecuteNow_AfterDispose_Throw()
    {
        var debouncer = Debouncer.CreateDirty(() => { }, TimeSpan.FromMilliseconds(30));
        await debouncer.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => debouncer.MarkDirty());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => debouncer.ExecuteNowAsync());
    }

    [Fact]
    public async Task DisposedInstance_ExitsLoopPromptly()
    {
        int runs = 0;
        var debouncer = Debouncer.CreateDirty(
            () => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(20));

        await debouncer.DisposeAsync();
        await Task.Delay(120);

        Assert.Equal(0, Volatile.Read(ref runs));               // 未标脏 → 循环退出前不执行
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DirtyDebouncer(null!, TimeSpan.FromSeconds(1), new()));
        Assert.Throws<ArgumentNullException>(
            () => new DirtyDebouncer(_ => Task.CompletedTask, TimeSpan.FromSeconds(1), null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DirtyDebouncer(_ => Task.CompletedTask, TimeSpan.Zero, new()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DirtyDebouncer(_ => Task.CompletedTask, TimeSpan.FromMilliseconds(-1), new()));
    }

    [Fact]
    public async Task SafeOnError_DefaultTrue_SwallowsCallbackException()
    {
        int runs = 0;
        var options = new DirtyDebouncer.Options
        {
            OnError = _ => throw new InvalidOperationException("回调自身异常")
        };
        await using var debouncer = new DirtyDebouncer(
            _ => { Interlocked.Increment(ref runs); throw new InvalidOperationException("落盘失败"); },
            TimeSpan.FromMilliseconds(30),
            options);

        debouncer.MarkDirty();

        // 回调异常被吞掉，轮询循环未被击穿，仍在继续重试
        Assert.True(await TestHelpers.WaitUntilAsync(() => Volatile.Read(ref runs) >= 2, TimeSpan.FromSeconds(5)));
    }
}
