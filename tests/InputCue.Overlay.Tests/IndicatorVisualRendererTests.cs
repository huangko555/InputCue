using InputCue.Core.Settings;

namespace InputCue.Overlay.Tests;

public sealed class IndicatorVisualRendererTests
{
    [Theory]
    [InlineData(IndicatorStyle.Default, 0.92)]
    [InlineData(IndicatorStyle.LightBadge, 1)]
    [InlineData(IndicatorStyle.ShadowBadge, 1)]
    public void EnglishUsGlyphCompressionOnlyAppliesToDefaultStyle(
        IndicatorStyle style,
        double expectedScale)
    {
        Assert.Equal(
            expectedScale,
            IndicatorVisualRenderer.CalculateGlyphVerticalScale(
                style,
                Core.InputContext.InputState.EnglishUs));
    }

    [Theory]
    [InlineData(IndicatorStyle.Dot, 12, 36, 18)]
    [InlineData(IndicatorStyle.Default, 12, 45, 45)]
    [InlineData(IndicatorStyle.LightBadge, 12, 45, 50)]
    [InlineData(IndicatorStyle.Custom, 12, 45, 50)]
    [InlineData(IndicatorStyle.Custom2, 12, 45, 50)]
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
