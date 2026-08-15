namespace InputCue.Windows.Tests;

public sealed class WindowsTestHostTests
{
    [Fact]
    public void TestHostRunsOnWindows()
    {
        Assert.True(OperatingSystem.IsWindows());
    }
}
