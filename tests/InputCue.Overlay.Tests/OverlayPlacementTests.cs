using InputCue.Core.InputContext;

namespace InputCue.Overlay.Tests;

public sealed class OverlayPlacementTests
{
    private static readonly PixelSize Overlay = new(18, 18);

    [Fact]
    public void PlacesOverlayToRightOfPhysicalPixelAnchor()
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(100, 200, 2, 20),
            new PixelRect(0, 0, 1920, 1040),
            Overlay,
            6);

        Assert.Equal(new PixelPoint(108, 202), result);
    }

    [Fact]
    public void MovesToLeftWhenRightEdgeWouldOverflow()
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(1916, 200, 2, 20),
            new PixelRect(0, 0, 1920, 1040),
            Overlay,
            6);

        Assert.Equal(new PixelPoint(1892, 202), result);
    }

    [Theory]
    [InlineData(2, 2, 10, 4)]
    [InlineData(100, 1038, 108, 1022)]
    public void ClampsOverlayAtTopAndBottomEdges(double anchorX, double anchorY, int expectedX, int expectedY)
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(anchorX, anchorY, 2, 2),
            new PixelRect(0, 4, 1920, 1040),
            Overlay,
            6);

        Assert.Equal(new PixelPoint(expectedX, expectedY), result);
    }

    [Fact]
    public void PreservesNegativeCoordinatesOnLeftHandMonitor()
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(-1000, 400, 2, 20),
            new PixelRect(-1920, 0, 0, 1040),
            Overlay,
            6);

        Assert.Equal(new PixelPoint(-992, 402), result);
    }

    [Theory]
    [InlineData(96, 6)]
    [InlineData(120, 8)]
    [InlineData(144, 9)]
    [InlineData(192, 12)]
    public void ScalesOnlyVisualGapForMonitorDpi(uint dpi, int expectedGap)
    {
        Assert.Equal(expectedGap, OverlayPlacement.ScaleDipToPixels(6, dpi));
    }
}
