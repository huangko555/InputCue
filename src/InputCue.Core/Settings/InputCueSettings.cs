using System.Text.Json.Serialization;
using InputCue.Core.Indicator;

namespace InputCue.Core.Settings;

public sealed record InputCueSettings(
    [property: JsonRequired] int DisplayDurationMilliseconds,
    [property: JsonRequired] int MinimumDisplayDurationMilliseconds,
    IndicatorPlacement Placement = IndicatorPlacement.BottomRight,
    int HorizontalOffsetDip = 0,
    int VerticalOffsetDip = 0,
    int IndicatorSizeDip = 12,
    IndicatorStyle Style = IndicatorStyle.Dot,
    int LightBadgeSizeDip = 36,
    string ChineseDotColor = "E5534B",
    string EnglishDotColor = "2F7FD6",
    string EnglishUsDotColor = "D99000",
    string CapsLockDotColor = "2F9E68",
    IndicatorAppearanceSettings? DotAppearance = null,
    IndicatorAppearanceSettings? LightBadgeAppearance = null,
    IndicatorAppearanceSettings? ShadowBadgeAppearance = null,
    AppStayPromptMode SameAppPromptMode = AppStayPromptMode.AfterDelay,
    int SameAppPromptDelaySeconds = 300,
    bool FullScreenAutoPause = true)
{
    public const IndicatorPlacement DefaultPlacement = IndicatorPlacement.BottomRight;
    public const int MinimumOffsetDip = -40;
    public const int MaximumOffsetDip = 40;
    public const int MinimumIndicatorSizeDip = 6;
    public const int MaximumIndicatorSizeDip = 32;
    public const int DefaultIndicatorSizeDip = 12;
    public const int MinimumLightBadgeSizeDip = 24;
    public const int MaximumLightBadgeSizeDip = 64;
    public const int DefaultLightBadgeSizeDip = 36;
    public const string DefaultChineseDotColor = "E5534B";
    public const string DefaultEnglishDotColor = "2F7FD6";
    public const string DefaultEnglishUsDotColor = "D99000";
    public const string DefaultCapsLockDotColor = "2F9E68";
    public const int DefaultSameAppPromptDelaySeconds = 300;
    public const int MinimumSameAppPromptDelaySeconds = 10;
    public const int MaximumSameAppPromptDelaySeconds = 3600;

    public static InputCueSettings Default { get; } = new(
        DisplayDurationMilliseconds: 1000,
        MinimumDisplayDurationMilliseconds: 300,
        Style: IndicatorStyle.LightBadge);

    [JsonIgnore]
    public bool IsValid =>
        DisplayDurationMilliseconds is >= 0 and <= 60000 &&
        MinimumDisplayDurationMilliseconds is >= 0 and <= 60000 &&
        Enum.IsDefined(Placement) &&
        HorizontalOffsetDip is >= MinimumOffsetDip and <= MaximumOffsetDip &&
        VerticalOffsetDip is >= MinimumOffsetDip and <= MaximumOffsetDip &&
        IndicatorSizeDip is >= MinimumIndicatorSizeDip and <= MaximumIndicatorSizeDip &&
        Enum.IsDefined(Style) &&
        LightBadgeSizeDip is >= MinimumLightBadgeSizeDip and <= MaximumLightBadgeSizeDip &&
        IsHexColor(ChineseDotColor) &&
        IsHexColor(EnglishDotColor) &&
        IsHexColor(EnglishUsDotColor) &&
        IsHexColor(CapsLockDotColor) &&
        IsValidAppearance(DotAppearance, IndicatorStyle.Dot) &&
        IsValidAppearance(LightBadgeAppearance, IndicatorStyle.LightBadge) &&
        IsValidAppearance(ShadowBadgeAppearance, IndicatorStyle.ShadowBadge) &&
        Enum.IsDefined(SameAppPromptMode) &&
        SameAppPromptDelaySeconds is >= MinimumSameAppPromptDelaySeconds
            and <= MaximumSameAppPromptDelaySeconds;

    public IndicatorAppearanceSettings GetAppearance(IndicatorStyle style) => style switch
    {
        IndicatorStyle.Dot => DotAppearance ?? new(
            Placement,
            HorizontalOffsetDip,
            VerticalOffsetDip,
            IndicatorSizeDip),
        IndicatorStyle.LightBadge => LightBadgeAppearance ?? new(
            Placement,
            HorizontalOffsetDip,
            VerticalOffsetDip,
            LightBadgeSizeDip),
        IndicatorStyle.ShadowBadge => ShadowBadgeAppearance ?? new(
            Placement,
            HorizontalOffsetDip,
            VerticalOffsetDip,
            LightBadgeSizeDip),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
    };

    public static bool IsHexColor(string? value) =>
        value is { Length: 6 } && value.All(Uri.IsHexDigit);

    private static bool IsValidAppearance(
        IndicatorAppearanceSettings? appearance,
        IndicatorStyle style) =>
        appearance is null ||
        Enum.IsDefined(appearance.Placement) &&
        appearance.HorizontalOffsetDip is >= MinimumOffsetDip and <= MaximumOffsetDip &&
        appearance.VerticalOffsetDip is >= MinimumOffsetDip and <= MaximumOffsetDip &&
        appearance.SizeDip >= (style == IndicatorStyle.Dot
            ? MinimumIndicatorSizeDip
            : MinimumLightBadgeSizeDip) &&
        appearance.SizeDip <= (style == IndicatorStyle.Dot
            ? MaximumIndicatorSizeDip
            : MaximumLightBadgeSizeDip);
}

public sealed record IndicatorAppearanceSettings(
    IndicatorPlacement Placement,
    int HorizontalOffsetDip,
    int VerticalOffsetDip,
    int SizeDip);
