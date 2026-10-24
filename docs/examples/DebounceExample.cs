using Lunarium.Common.Debounce;

namespace Lunarium.Common.Examples;

internal static class DebounceExample
{
    public static async Task RunAsync()
    {
        ExampleSupport.Section("CtsDebouncer / DirtyDebouncer");
        await RunCtsAsync();
        await RunDirtyAsync();
    }

    private static async Task RunCtsAsync()
    {
        int runs = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using (var debouncer = Debouncer.CreateCts(
            _ =>
            {
                Interlocked.Increment(ref runs);
                done.TrySetResult(true);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(100)))
        {
            for (int i = 0; i < 5; i++)
                debouncer.Invoke();

            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Console.WriteLine($"CtsDebouncer runs = {runs} (expected 1)");
        if (runs != 1)
            throw new InvalidOperationException($"CtsDebouncer 应只执行一次，实际 {runs} 次。");
    }

    private static async Task RunDirtyAsync()
    {
        int runs = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using (var saver = Debouncer.CreateDirty(
            _ =>
            {
                Interlocked.Increment(ref runs);
                done.TrySetResult(true);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(100)))
        {
            saver.MarkDirty();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Console.WriteLine($"DirtyDebouncer runs = {runs} (expected 1)");
        if (runs != 1)
            throw new InvalidOperationException($"DirtyDebouncer 应只执行一次，实际 {runs} 次。");
    }
}
