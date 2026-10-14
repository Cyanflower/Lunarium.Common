using Lunarium.Common.Debounce;

namespace Lunarium.Common.Tests;

public class SafeOnErrorTests
{
    [Fact]
    public void SafeInvoke_NullAction_DoesNotThrow()
    {
        Action<Exception>? nullAction = null;
        Assert.Null(Record.Exception(() => nullAction.SafeInvoke(new InvalidOperationException())));
    }

    [Fact]
    public void SafeInvoke_PassesOriginalExceptionToCallback()
    {
        Exception? received = null;
        Action<Exception> capture = e => received = e;
        var original = new InvalidOperationException("原始异常");

        capture.SafeInvoke(original);

        Assert.Same(original, received);
    }

    [Fact]
    public void SafeInvoke_SafeTrue_SwallowsCallbackException()
    {
        Action<Exception> throwing = _ => throw new InvalidOperationException("回调自身异常");
        Assert.Null(Record.Exception(() => throwing.SafeInvoke(new Exception("原始异常"), safe: true)));
    }

    [Fact]
    public void SafeInvoke_SafeFalse_PropagatesCallbackException()
    {
        Action<Exception> throwing = _ => throw new InvalidOperationException("回调自身异常");
        var ex = Assert.Throws<InvalidOperationException>(
            () => throwing.SafeInvoke(new Exception("原始异常"), safe: false));
        Assert.Equal("回调自身异常", ex.Message);
    }
}
