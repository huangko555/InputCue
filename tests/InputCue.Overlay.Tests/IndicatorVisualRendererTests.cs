using InputCue.Core.Settings;

namespace InputCue.Overlay.Tests;

public sealed class IndicatorVisualRendererTests
{
    [Theory]
    [InlineData(IndicatorStyle.Dot, 12, 36, 18)]
    [InlineData(IndicatorStyle.LightBadge, 12, 45, 50)]
    [InlineData(IndicatorStyle.Custom, 12, 45, 50)]
    [InlineData(IndicatorStyle.ShadowBadge, 12, 45, 57.5)]
    public void RootSizeReservesPaddingForTheStyleDecoration(
        IndicatorStyle style,
        int dotSizeDip,
        int badgeSizeDip,
        double expectedSizeDip)
    {
        var size = IndicatorVisualRenderer.CalculateRootSize(style, dotSizeDip, badgeSizeDip);

        Assert.Equal(expectedSizeDip, size, precision: 2);
    }
}
