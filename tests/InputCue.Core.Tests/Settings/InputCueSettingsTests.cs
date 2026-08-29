using InputCue.Core.Indicator;
using InputCue.Core.Settings;

namespace InputCue.Core.Tests.Settings;

public sealed class InputCueSettingsTests
{
    [Fact]
    public void MissingCustomAppearanceFallsBackToLegacyBadgeDefaults()
    {
        var settings = new InputCueSettings(1000, 300);

        var appearance = settings.GetAppearance(IndicatorStyle.Custom);

        Assert.Equal(settings.Placement, appearance.Placement);
        Assert.Equal(settings.HorizontalOffsetDip, appearance.HorizontalOffsetDip);
        Assert.Equal(settings.VerticalOffsetDip, appearance.VerticalOffsetDip);
        Assert.Equal(settings.LightBadgeSizeDip, appearance.SizeDip);
        Assert.True(settings.IsValid);
    }

    [Fact]
    public void ExplicitCustomAppearanceIsReturnedForCustomStyle()
    {
        var appearance = new IndicatorAppearanceSettings(
            IndicatorPlacement.TopLeft,
            -6,
            4,
            52);
        var settings = new InputCueSettings(1000, 300, CustomAppearance: appearance);

        Assert.Equal(appearance, settings.GetAppearance(IndicatorStyle.Custom));
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void CustomAppearanceSizeMustRespectBadgeBounds(int sizeDip, bool expectedValid)
    {
        var settings = new InputCueSettings(
            1000,
            300,
            CustomAppearance: new(IndicatorPlacement.BottomRight, 0, 0, sizeDip));

        Assert.Equal(expectedValid, settings.IsValid);
    }
}
