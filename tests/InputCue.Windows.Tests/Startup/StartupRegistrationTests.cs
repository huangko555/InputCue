using InputCue.Windows.Startup;

namespace InputCue.Windows.Tests.Startup;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void BuildCommandQuotesExecutableAndUsesBackgroundMode()
    {
        var executable = Path.Combine(
            Path.GetTempPath(),
            "InputCue Folder",
            "InputCue.exe");

        var command = StartupRegistration.BuildCommand(executable);

        Assert.Equal($"\"{Path.GetFullPath(executable)}\" --background", command);
    }
}
