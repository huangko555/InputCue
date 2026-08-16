namespace InputCue.Overlay;

internal static class OverlayRenderPolicy
{
    internal static bool ShouldReposition(
        bool isVisible,
        long? positionedGeneration,
        long generation) =>
        !isVisible || positionedGeneration != generation;
}
