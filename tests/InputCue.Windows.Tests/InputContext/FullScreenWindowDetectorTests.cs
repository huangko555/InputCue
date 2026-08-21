using InputCue.Windows.InputContext;
using InputCue.Windows.Interop;

namespace InputCue.Windows.Tests.InputContext;

public sealed class FullScreenWindowDetectorTests
{
    [Fact]
    public void ExactMonitorBoundsAreFullScreen()
    {
        Assert.True(FullScreenWindowDetector.IsFullScreenBounds(
            Rect(0, 0, 1920, 1080),
            Rect(0, 0, 1920, 1080)));
    }

    [Fact]
    public void SmallNativeBorderDifferenceIsTolerated()
    {
        Assert.True(FullScreenWindowDetector.IsFullScreenBounds(
            Rect(-2, -1, 1922, 1081),
            Rect(0, 0, 1920, 1080)));
    }

    [Fact]
    public void NormalMaximizedWindowAboveTaskbarIsNotFullScreen()
    {
        Assert.False(FullScreenWindowDetector.IsFullScreenBounds(
            Rect(0, 0, 1920, 1040),
            Rect(0, 0, 1920, 1080)));
    }

    [Fact]
    public void SecondaryMonitorCoordinatesAreSupported()
    {
        Assert.True(FullScreenWindowDetector.IsFullScreenBounds(
            Rect(-1920, 0, 0, 1080),
            Rect(-1920, 0, 0, 1080)));
    }

    private static NativeRect Rect(int left, int top, int right, int bottom) => new()
    {
        Left = left,
        Top = top,
        Right = right,
        Bottom = bottom,
    };
}
