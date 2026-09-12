namespace InputCue.Core.Indicator;

/// <summary>
/// Defines transient display timing, or keeps the indicator visible while the context remains eligible.
/// </summary>
public sealed record IndicatorSessionOptions(
    TimeSpan DisplayDuration,
    TimeSpan FadeDuration,
    bool AlwaysVisible = false)
{
    public IndicatorDisplayMode DisplayMode { get; init; } = IndicatorDisplayMode.Transient;

    public TimeSpan IdleReshowDelay { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan MinimumDisplayDuration { get; init; } = TimeSpan.FromMilliseconds(300);

    public TimeSpan ContextReplaySuppressionDuration { get; init; } = TimeSpan.FromMilliseconds(1500);

    public TimeSpan ContextLossGracePeriod { get; init; } = TimeSpan.FromMilliseconds(1200);

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

        if (MinimumDisplayDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumDisplayDuration),
                MinimumDisplayDuration,
                "Minimum display duration cannot be negative.");
        }

        if (!Enum.IsDefined(DisplayMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(DisplayMode),
                DisplayMode,
                "Display mode must be defined.");
        }

        if (IdleReshowDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(IdleReshowDelay),
                IdleReshowDelay,
                "Idle reshow delay cannot be negative.");
        }

        if (ContextReplaySuppressionDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ContextReplaySuppressionDuration),
                ContextReplaySuppressionDuration,
                "Context replay suppression duration cannot be negative.");
        }

        if (ContextLossGracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ContextLossGracePeriod),
                ContextLossGracePeriod,
                "Context loss grace period cannot be negative.");
        }
    }
}
