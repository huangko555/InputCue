using InputCue.Core.Indicator;
using InputCue.Core.Settings;

namespace InputCue.Core.Tests.Settings;

public sealed class InputCueSettingsTests
{
    [Theory]
    [InlineData(IndicatorStyle.Default, IndicatorPlacement.Top, 22, 0, 4, IndicatorTransitionAnimation.Flip)]
    [InlineData(IndicatorStyle.Dot, IndicatorPlacement.Bottom, 36, 0, 0, IndicatorTransitionAnimation.None)]
    [InlineData(IndicatorStyle.LightBadge, IndicatorPlacement.BottomRight, 36, 0, 0, IndicatorTransitionAnimation.None)]
    [InlineData(IndicatorStyle.ShadowBadge, IndicatorPlacement.BottomRight, 36, 0, 0, IndicatorTransitionAnimation.None)]
    [InlineData(IndicatorStyle.Custom, IndicatorPlacement.BottomRight, 36, 0, 0, IndicatorTransitionAnimation.None)]
    [InlineData(IndicatorStyle.Custom2, IndicatorPlacement.BottomRight, 36, 0, 0, IndicatorTransitionAnimation.None)]
    public void BuiltInAppearanceDefaultsAreDefinedPerStyle(
        IndicatorStyle style,
        IndicatorPlacement expectedPlacement,
        int expectedSizeDip,
        int expectedHorizontalOffsetDip,
        int expectedVerticalOffsetDip,
        IndicatorTransitionAnimation expectedAnimation)
    {
        var expected = new IndicatorAppearanceSettings(
            expectedPlacement,
            expectedHorizontalOffsetDip,
            expectedVerticalOffsetDip,
            expectedSizeDip,
            expectedAnimation);

        Assert.Equal(expected, InputCueSettings.DefaultAppearanceFor(style));
        Assert.Equal(expected, InputCueSettings.Default.GetAppearance(style));
    }

    [Fact]
    public void StyleCatalogCoversEveryStyleExactlyOnce()
    {
        var styles = IndicatorStyleCatalog.All.Select(definition => definition.Style).ToArray();

        Assert.Equal(Enum.GetValues<IndicatorStyle>().Length, styles.Length);
        Assert.Equal(styles.Length, styles.Distinct().Count());
        Assert.All(Enum.GetValues<IndicatorStyle>(), style =>
            Assert.Equal(style, IndicatorStyleCatalog.Get(style).Style));
    }

    [Fact]
    public void DotDefaultsUseRequestedPlacementAndSizeRange()
    {
        var definition = IndicatorStyleCatalog.Get(IndicatorStyle.Dot);

        Assert.True(definition.UsesDotSize);
        Assert.False(definition.IsCustom);
        Assert.Equal(IndicatorPlacement.Bottom, definition.DefaultAppearance.Placement);
        Assert.Equal(36, definition.DefaultAppearance.SizeDip);
        Assert.Equal(64, definition.MaximumSizeDip);
    }

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

    [Fact]
    public void DefaultAndCustom2AppearancesAreIndependent()
    {
        var defaultAppearance = new IndicatorAppearanceSettings(
            IndicatorPlacement.Left,
            1,
            2,
            32);
        var custom2Appearance = new IndicatorAppearanceSettings(
            IndicatorPlacement.Right,
            3,
            4,
            56);
        var settings = new InputCueSettings(
            1000,
            300,
            DefaultAppearance: defaultAppearance,
            Custom2Appearance: custom2Appearance);

        Assert.Equal(defaultAppearance, settings.GetAppearance(IndicatorStyle.Default));
        Assert.Equal(custom2Appearance, settings.GetAppearance(IndicatorStyle.Custom2));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
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

    [Fact]
    public void AppearanceTransitionAnimationMustBeDefined()
    {
        var settings = new InputCueSettings(
            1000,
            300,
            DotAppearance: new(
                IndicatorPlacement.BottomRight,
                0,
                0,
                12,
                (IndicatorTransitionAnimation)99));

        Assert.False(settings.IsValid);
    }
}
