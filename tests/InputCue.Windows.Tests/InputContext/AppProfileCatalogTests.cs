using System.Windows.Automation;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class AppProfileCatalogTests
{
    [Fact]
    public void WpsDocumentSurfaceRequiresTheRecordedWritableShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableWpsDocumentSurface(
            "wps",
            "KxWpsView",
            "Qt",
            ControlType.Group,
            hasValuePattern: true));
    }

    [Theory]
    [InlineData("other", "KxWpsView", "Qt", true)]
    [InlineData("wps", "OtherView", "Qt", true)]
    [InlineData("wps", "KxWpsView", "Win32", true)]
    [InlineData("wps", "KxWpsView", "Qt", false)]
    public void WpsDocumentSurfaceRejectsNearMisses(
        string processName,
        string className,
        string frameworkId,
        bool hasValuePattern)
    {
        Assert.False(AppProfileCatalog.SupportsWritableWpsDocumentSurface(
            processName,
            className,
            frameworkId,
            ControlType.Group,
            hasValuePattern));
    }

    [Fact]
    public void WindowsTerminalSurfaceRequiresTheRecordedTextShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableWindowsTerminalSurface(
            "WindowsTerminal",
            "TermControl",
            "XAML",
            ControlType.Text));
    }

    [Theory]
    [InlineData("other", "TermControl", "XAML")]
    [InlineData("WindowsTerminal", "OtherControl", "XAML")]
    [InlineData("WindowsTerminal", "TermControl", "Win32")]
    public void WindowsTerminalSurfaceRejectsNearMisses(
        string processName,
        string className,
        string frameworkId)
    {
        Assert.False(AppProfileCatalog.SupportsWritableWindowsTerminalSurface(
            processName,
            className,
            frameworkId,
            ControlType.Text));
    }

    [Fact]
    public void WindowsTerminalSurfaceRejectsNonTextControl()
    {
        Assert.False(AppProfileCatalog.SupportsWritableWindowsTerminalSurface(
            "WindowsTerminal",
            "TermControl",
            "XAML",
            ControlType.Group));
    }
}
