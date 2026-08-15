namespace InputCue.Core.Indicator;

/// <summary>
/// Defines transient display timing, or keeps the indicator visible while the context remains eligible.
/// </summary>
public sealed record IndicatorSessionOptions(
    TimeSpan DisplayDuration,
    TimeSpan FadeDuration,
    bool AlwaysVisible = false)
{
    public static IndicatorSessionOptions Default { get; } = new(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(150));

    internal void Validate()
    {
        if (DisplayDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DisplayDuration),
                DisplayDuration,
                "Display duration cannot be negative.");
        }

        if (FadeDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FadeDuration),
                FadeDuration,
                "Fade duration cannot be negative.");
        }
    }
}
