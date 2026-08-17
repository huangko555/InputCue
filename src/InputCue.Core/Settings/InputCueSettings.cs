using System.Text.Json.Serialization;

namespace InputCue.Core.Settings;

public sealed record InputCueSettings(
    [property: JsonRequired] bool IndicatorEnabled,
    [property: JsonRequired] int DisplayDurationMilliseconds,
    [property: JsonRequired] int MinimumDisplayDurationMilliseconds,
    IndicatorPlacement Placement = IndicatorPlacement.Right,
    int HorizontalOffsetDip = 0,
    int VerticalOffsetDip = 0,
    int IndicatorSizeDip = 12)
{
    public const int MinimumOffsetDip = -40;
    public const int MaximumOffsetDip = 40;
    public const int MinimumIndicatorSizeDip = 6;
    public const int MaximumIndicatorSizeDip = 32;
    public const int DefaultIndicatorSizeDip = 12;

    public static InputCueSettings Default { get; } = new(
        IndicatorEnabled: true,
        DisplayDurationMilliseconds: 1000,
        MinimumDisplayDurationMilliseconds: 300);

    [JsonIgnore]
    public bool IsValid =>
        DisplayDurationMilliseconds is >= 0 and <= 60000 &&
        MinimumDisplayDurationMilliseconds is >= 0 and <= 60000 &&
        Enum.IsDefined(Placement) &&
        HorizontalOffsetDip is >= MinimumOffsetDip and <= MaximumOffsetDip &&
        VerticalOffsetDip is >= MinimumOffsetDip and <= MaximumOffsetDip &&
        IndicatorSizeDip is >= MinimumIndicatorSizeDip and <= MaximumIndicatorSizeDip;
}
