namespace InputCue.Overlay;

using InputCue.Core.Indicator;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;

internal static class OverlayRenderPolicy
{
    internal static bool ShouldReposition(
        bool isVisible,
        long? positionedGeneration,
        long generation,
        ScreenRect? positionedAnchor,
        ScreenRect anchor,
        bool contextActivated = false) =>
        contextActivated ||
        !isVisible ||
        positionedGeneration != generation ||
        positionedAnchor != anchor;

    internal static bool ShouldPreserveZOrder(
        bool isVisible,
        bool targetChanged,
        bool contextActivated = false) =>
        !contextActivated && isVisible && !targetChanged;

    internal static bool ShouldAnimateTransition(
        IndicatorTransitionAnimation animation,
        bool isVisible,
        InputState? renderedState,
        InputState nextState,
        IndicatorReasonCode reasonCode) =>
        animation == IndicatorTransitionAnimation.Flip &&
        isVisible &&
        renderedState is { } currentState &&
        currentState != nextState &&
        reasonCode == IndicatorReasonCode.InputStateChanged;

    internal static bool ShouldAnimateAppearance(
        IndicatorTransitionAnimation animation,
        bool isVisible) =>
        animation == IndicatorTransitionAnimation.Flip && !isVisible;
}
