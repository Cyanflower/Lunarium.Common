using System.Collections.Concurrent;
using Lunarium.Common.Debounce;

namespace Lunarium.Common.Tests;

public class CtsDebouncerTests
{
    [Fact]
    public async Task Invoke_BurstIsCollapsedIntoOneRun()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateCts(
            () => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(40));

        for (int i = 0; i < 10; i++) debouncer.Invoke();

        Assert.True(await TestHelpers.WaitUntilAsync(() => Volatile.Read(ref runs) == 1, TimeSpan.FromSeconds(3)));
        await Task.Delay(120);
        Assert.Equal(1, Volatile.Read(ref runs));               // 尾沿只触发一次
    }

    [Fact]
    public async Task Invoke_AfterDelay_TriggersAgain()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateCts(
            () => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(30));

        debouncer.Invoke();
        Assert.True(await TestHelpers.WaitUntilAsync(() => Volatile.Read(ref runs) == 1, TimeSpan.FromSeconds(3)));

        debouncer.Invoke();
        Assert.True(await TestHelpers.WaitUntilAsync(() => Volatile.Read(ref runs) == 2, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task ExecuteNowAsync_RunsImmediatelyWithoutWaitingDelay()
    {
        int runs = 0;
        await using var debouncer = Debouncer.CreateCts(
            () => Interlocked.Increment(ref runs), TimeSpan.FromSeconds(30));

        debouncer.Invoke();
        await debouncer.ExecuteNowAsync();

        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task Actions_AreSerialized_NeverRunConcurrently()
    {
        int concurrent = 0;
        int maxConcurrent = 0;
        int completed = 0;

        await using var debouncer = Debouncer.CreateCts(async _ =>
        {
            int now = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref concurrent);
            Interlocked.Increment(ref completed);
        }, TimeSpan.FromMilliseconds(10));

        debouncer.Invoke();
        await Task.Delay(40);
        debouncer.Invoke();
        await debouncer.ExecuteNowAsync();
        await debouncer.ExecuteNowAsync();

        Assert.Equal(1, Volatile.Read(ref maxConcurrent));
        Assert.Equal(0, Volatile.Read(ref concurrent));
        Assert.True(Volatile.Read(ref completed) >= 1);
    }

    [Fact]
    public async Task MaxWait_ForcesExecutionDuringContinuousCalls()
    {
        int runs = 0;
        var options = new CtsDebouncer.Options { MaxWait = TimeSpan.FromMilliseconds(120) };
        await using var debouncer = new CtsDebouncer(
            _ => { Interlocked.Increment(ref runs); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(200),
            options);

        DateTime stop = DateTime.UtcNow.AddMilliseconds(600);
        while (DateTime.UtcNow < stop)
        {
            debouncer.Invoke();
            await Task.Delay(10);
        }

        Assert.True(Volatile.Read(ref runs) >= 2, $"MaxWait 未生效，600ms 内仅执行 {runs} 次");
    }

    [Fact]
    public async Task Action_ReceivesExternalToken()
    {
        CancellationToken? captured = null;
        using var external = new CancellationTokenSource();
        var options = new CtsDebouncer.Options { ActionCancellationToken = external.Token };

        await using var debouncer = new CtsDebouncer(
            ct => { captured = ct; return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(20),
            options);

        debouncer.Invoke();
        Assert.True(await TestHelpers.WaitUntilAsync(() => captured.HasValue, TimeSpan.FromSeconds(3)));
        Assert.Equal(external.Token, captured!.Value);
    }

    [Fact]
    public async Task ExternalTokenCancellation_DoesNotPropagateOutOfDebouncer()
    {
        using var external = new CancellationTokenSource();
        var options = new CtsDebouncer.Options { ActionCancellationToken = external.Token };

        await using var debouncer = new CtsDebouncer(
            ct => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(20),
            options);

        external.Cancel();

        debouncer.Invoke();
        await debouncer.ExecuteNowAsync();                      // action 抛 OCE，被防抖器吞掉，不外泄
    }

    [Fact]
    public async Task DisposeAsync_WaitsForInFlightAction()
    {
        bool started = false;
        bool finished = false;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var debouncer = Debouncer.CreateCts(async (CancellationToken _) =>
        {
            started = true;
            await gate.Task;
            finished = true;
        }, TimeSpan.FromMilliseconds(20));

        debouncer.Invoke();
        Assert.True(await TestHelpers.WaitUntilAsync(() => started, TimeSpan.FromSeconds(3)));

        Task disposeTask = debouncer.DisposeAsync().AsTask();
        await Task.Delay(80);
        Assert.False(disposeTask.IsCompleted);                  // 仍在等待在途 action

        gate.SetResult();
        await disposeTask;

        Assert.True(finished);
    }

    [Fact]
    public void Dispose_DoesNotCancelExternalToken()
    {
        using var external = new CancellationTokenSource();
        var options = new CtsDebouncer.Options { ActionCancellationToken = external.Token };
        var debouncer = new CtsDebouncer(_ => Task.CompletedTask, TimeSpan.FromMilliseconds(20), options);

        debouncer.Dispose();

        Assert.False(external.IsCancellationRequested);
    }

    [Fact]
    public async Task InvokeAndExecuteNow_AfterDispose_Throw()
    {
        var debouncer = Debouncer.CreateCts(() => { }, TimeSpan.FromMilliseconds(20));
        debouncer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => debouncer.Invoke());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => debouncer.ExecuteNowAsync());
    }

    [Fact]
    public async Task OnError_IsInvokedAndDoesNotKillSubsequentRuns()
    {
        var errors = new ConcurrentQueue<Exception>();
        int runs = 0;
        var options = new CtsDebouncer.Options { OnError = errors.Enqueue };

        await using var debouncer = new CtsDebouncer(async _ =>
        {
            await Task.Yield();
            if (Interlocked.Increment(ref runs) == 1)
                throw new InvalidOperationException("首次失败");
        }, TimeSpan.FromMilliseconds(20), options);

        debouncer.Invoke();
        Assert.True(await TestHelpers.WaitUntilAsync(() => errors.Count == 1, TimeSpan.FromSeconds(3)));

        debouncer.Invoke();
        Assert.True(await TestHelpers.WaitUntilAsync(() => Volatile.Read(ref runs) >= 2, TimeSpan.FromSeconds(3)));
        Assert.Single(errors);
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CtsDebouncer(null!, TimeSpan.FromSeconds(1), new()));
        Assert.Throws<ArgumentNullException>(
            () => new CtsDebouncer(_ => Task.CompletedTask, TimeSpan.FromSeconds(1), null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CtsDebouncer(_ => Task.CompletedTask, TimeSpan.Zero, new()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CtsDebouncer(_ => Task.CompletedTask, TimeSpan.FromMilliseconds(-1), new()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CtsDebouncer(_ => Task.CompletedTask, TimeSpan.FromSeconds(1), new() { MaxWait = TimeSpan.Zero }));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current) return;
            current = seen;
        }
    }
}
