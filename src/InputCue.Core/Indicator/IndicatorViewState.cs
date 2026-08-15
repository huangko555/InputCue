using InputCue.Core.InputContext;

namespace InputCue.Core.Indicator;

/// <summary>
/// Contains only the information required by an overlay renderer; it has no Windows or WPF dependency.
/// </summary>
public sealed record IndicatorViewState(
    long Generation,
    IndicatorPhase Phase,
    InputState InputState,
    ScreenRect? Anchor,
    double Opacity,
    IndicatorReasonCode ReasonCode)
{
    public bool IsVisible => Phase is not IndicatorPhase.Hidden;

    internal static IndicatorViewState Initial { get; } = Hidden(
        generation: 0,
        IndicatorReasonCode.Initial);

    internal static IndicatorViewState Hidden(
        long generation,
        IndicatorReasonCode reasonCode) =>
        new(
            generation,
            IndicatorPhase.Hidden,
            InputState.Unknown,
            null,
            0,
            reasonCode);
}
