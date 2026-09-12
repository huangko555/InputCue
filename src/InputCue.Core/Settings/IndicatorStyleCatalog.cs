namespace InputCue.Core.Settings;

public sealed record IndicatorStyleDefinition(
    IndicatorStyle Style,
    string DisplayName,
    bool UsesDotSize,
    bool IsCustom,
    IndicatorAppearanceSettings DefaultAppearance)
{
    public int MinimumSizeDip => UsesDotSize
        ? InputCueSettings.MinimumIndicatorSizeDip
        : InputCueSettings.MinimumLightBadgeSizeDip;

    public int MaximumSizeDip => UsesDotSize
        ? InputCueSettings.MaximumIndicatorSizeDip
        : InputCueSettings.MaximumLightBadgeSizeDip;
}

public static class IndicatorStyleCatalog
{
    private static readonly IndicatorStyleDefinition[] Definitions =
    [
        new(
            IndicatorStyle.Default,
            "默认",
            UsesDotSize: false,
            IsCustom: false,
            new(
                InputCueSettings.DefaultStylePlacement,
                0,
                InputCueSettings.DefaultStyleVerticalOffsetDip,
                InputCueSettings.DefaultStyleBadgeSizeDip,
                IndicatorTransitionAnimation.Flip)),
        new(
            IndicatorStyle.LightBadge,
            "描边",
            UsesDotSize: false,
            IsCustom: false,
            new(
                InputCueSettings.DefaultPlacement,
                0,
                0,
                InputCueSettings.DefaultLightBadgeSizeDip)),
        new(
            IndicatorStyle.ShadowBadge,
            "阴影",
            UsesDotSize: false,
            IsCustom: false,
            new(
                InputCueSettings.DefaultPlacement,
                0,
                0,
                InputCueSettings.DefaultLightBadgeSizeDip)),
        new(
            IndicatorStyle.Dot,
            "圆点",
            UsesDotSize: true,
            IsCustom: false,
            new(
                IndicatorPlacement.Bottom,
                0,
                0,
                InputCueSettings.DefaultIndicatorSizeDip)),
        new(
            IndicatorStyle.Custom,
            "自定义1",
            UsesDotSize: false,
            IsCustom: true,
            new(
                InputCueSettings.DefaultPlacement,
                0,
                0,
                InputCueSettings.DefaultLightBadgeSizeDip)),
        new(
            IndicatorStyle.Custom2,
            "自定义2",
            UsesDotSize: false,
            IsCustom: true,
            new(
                InputCueSettings.DefaultPlacement,
                0,
                0,
                InputCueSettings.DefaultLightBadgeSizeDip)),
    ];

    private static readonly Dictionary<IndicatorStyle, IndicatorStyleDefinition> ByStyle =
        Definitions.ToDictionary(definition => definition.Style);

    public static IReadOnlyList<IndicatorStyleDefinition> All { get; } =
        Array.AsReadOnly(Definitions);

    public static IndicatorStyleDefinition Get(IndicatorStyle style) =>
        ByStyle.TryGetValue(style, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(style), style, null);
}
