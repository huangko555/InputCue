using InputCue.Core.InputContext;
using InputCue.Core.Settings;

namespace InputCue.Overlay.Tests;

public sealed class OverlayPlacementTests
{
    private static readonly PixelSize Overlay = new(18, 18);

    [Theory]
    [InlineData(IndicatorPlacement.TopLeft, 76, 176)]
    [InlineData(IndicatorPlacement.Top, 92, 176)]
    [InlineData(IndicatorPlacement.TopRight, 108, 176)]
    [InlineData(IndicatorPlacement.Left, 76, 201)]
    [InlineData(IndicatorPlacement.Right, 108, 201)]
    [InlineData(IndicatorPlacement.BottomLeft, 76, 226)]
    [InlineData(IndicatorPlacement.Bottom, 92, 226)]
    [InlineData(IndicatorPlacement.BottomRight, 108, 226)]
    public void PlacesOverlayInEachSupportedDirection(
        IndicatorPlacement placement,
        int expectedX,
        int expectedY)
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(100, 200, 2, 20),
            new PixelRect(0, 0, 1920, 1040),
            Overlay,
            6,
            placement);

        Assert.Equal(new PixelPoint(expectedX, expectedY), result);
    }

    [Fact]
    public void MovesToLeftWhenRightEdgeWouldOverflow()
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(1916, 200, 2, 20),
            new PixelRect(0, 0, 1920, 1040),
            Overlay,
            6);

        Assert.Equal(new PixelPoint(1892, 201), result);
    }

    [Theory]
    [InlineData(IndicatorPlacement.Top, 100, 4, 92, 12)]
    [InlineData(IndicatorPlacement.Bottom, 100, 1038, 92, 1014)]
    public void UsesOppositeDirectionAtWorkAreaEdge(
        IndicatorPlacement placement,
        double anchorX,
        double anchorY,
        int expectedX,
        int expectedY)
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(anchorX, anchorY, 2, 2),
            new PixelRect(0, 4, 1920, 1040),
            Overlay,
            6,
            placement);

        Assert.Equal(new PixelPoint(expectedX, expectedY), result);
    }

    [Fact]
    public void AppliesFineTuningAfterSelectingDirection()
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(100, 200, 2, 20),
            new PixelRect(0, 0, 1920, 1040),
            Overlay,
            6,
            IndicatorPlacement.Right,
            horizontalOffset: 7,
            verticalOffset: -4);

        Assert.Equal(new PixelPoint(115, 197), result);
    }

    [Fact]
    public void PreservesNegativeCoordinatesOnLeftHandMonitor()
    {
        var result = OverlayPlacement.Calculate(
            new ScreenRect(-1000, 400, 2, 20),
            new PixelRect(-1920, 0, 0, 1040),
            Overlay,
            6);

        Assert.Equal(new PixelPoint(-992, 401), result);
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
