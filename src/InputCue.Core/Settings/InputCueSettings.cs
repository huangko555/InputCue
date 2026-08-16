using System.Text.Json.Serialization;

namespace InputCue.Core.Settings;

public sealed record InputCueSettings(
    [property: JsonRequired] bool IndicatorEnabled,
    [property: JsonRequired] int DisplayDurationMilliseconds,
    [property: JsonRequired] int MinimumDisplayDurationMilliseconds)
{
    public static InputCueSettings Default { get; } = new(
        IndicatorEnabled: true,
        DisplayDurationMilliseconds: 1000,
        MinimumDisplayDurationMilliseconds: 300);

    [JsonIgnore]
    public bool IsValid =>
        DisplayDurationMilliseconds is >= 0 and <= 60000 &&
        MinimumDisplayDurationMilliseconds is >= 0 and <= 60000;
}
