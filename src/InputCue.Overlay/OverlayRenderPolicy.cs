namespace InputCue.Overlay;

using InputCue.Core.InputContext;

internal static class OverlayRenderPolicy
{
    internal static bool ShouldReposition(
        bool isVisible,
        long? positionedGeneration,
        long generation,
        ScreenRect? positionedAnchor,
        ScreenRect anchor) =>
        !isVisible || positionedGeneration != generation || positionedAnchor != anchor;
}
