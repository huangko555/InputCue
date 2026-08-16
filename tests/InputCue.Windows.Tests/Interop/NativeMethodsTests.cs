using InputCue.Windows.Interop;

namespace InputCue.Windows.Tests.Interop;

public sealed class NativeMethodsTests
{
    [Fact]
    public void GetCurrentThreadIdResolvesFromWindows()
    {
        var threadId = NativeMethods.GetCurrentThreadId();

        Assert.NotEqual(0u, threadId);
    }
}
