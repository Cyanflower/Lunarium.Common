namespace Lunarium.Common.Debounce;

internal static class SafeOnError
{
    /// <summary>
    /// 安全地调用异常处理回调, 防御性捕获异常, 避免用户注册的回调自身异常击穿到未捕获线程池
    /// </summary>
    /// <param name="action">OnError 回调</param>
    /// <param name="ex">异常</param>
    /// <param name="safe">是否安全回调, true = 安全回调(不可观测), onError本身异常静默吞掉, false = 不安全回调(可观测), onError异常会抛出</param>
    public static void SafeInvoke(this Action<Exception>? action, Exception ex, bool safe = true)
    {
        if (safe == false)
        {
            action?.Invoke(ex);
        }
        else
        {
            try
            {
                action?.Invoke(ex);
            }
            catch (Exception callbackEx)
            {
                // 防御性捕获：防止回调自身异常击穿到未捕获线程池
                System.Diagnostics.Debug.WriteLine($"[Debounce] SafeInvoke swallowed exception: {callbackEx}");
            }
        }
    }
}