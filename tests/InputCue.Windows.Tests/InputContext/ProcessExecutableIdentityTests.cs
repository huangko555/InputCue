using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class ProcessExecutableIdentityTests
{
    [Fact]
    public void IsCompatibleAcceptsTheSameProcessWithoutReadingPaths()
    {
        var identity = new ProcessExecutableIdentity(_ =>
            throw new InvalidOperationException("Path lookup should not be needed."));

        Assert.True(identity.IsCompatible(42, 42));
    }

    [Fact]
    public void IsCompatibleAcceptsDifferentProcessesFromTheSameExecutable()
    {
        var identity = new ProcessExecutableIdentity(_ => @"C:\Program Files\WPS\wps.exe");

        Assert.True(identity.IsCompatible(42, 43));
    }

    [Fact]
    public void IsCompatibleRejectsDifferentExecutables()
    {
        var identity = new ProcessExecutableIdentity(processId => processId == 42
            ? @"C:\Program Files\WPS\wps.exe"
            : @"C:\Program Files\Other\other.exe");

        Assert.False(identity.IsCompatible(42, 43));
    }

    [Theory]
    [InlineData(null, @"C:\Program Files\WPS\wps.exe")]
    [InlineData(@"C:\Program Files\WPS\wps.exe", null)]
    [InlineData("", @"C:\Program Files\WPS\wps.exe")]
    public void IsCompatibleRejectsMissingExecutableEvidence(
        string? foregroundPath,
        string? focusedPath)
    {
        var identity = new ProcessExecutableIdentity(processId => processId == 42
            ? foregroundPath
            : focusedPath);

        Assert.False(identity.IsCompatible(42, 43));
    }
}
