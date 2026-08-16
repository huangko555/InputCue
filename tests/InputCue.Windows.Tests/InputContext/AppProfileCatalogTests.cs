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
}
