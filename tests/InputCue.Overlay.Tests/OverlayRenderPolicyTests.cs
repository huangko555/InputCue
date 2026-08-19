namespace InputCue.Overlay.Tests;

using InputCue.Core.InputContext;

public sealed class OverlayRenderPolicyTests
{
    private static readonly ScreenRect Anchor = new(100, 120, 2, 20);

    [Fact]
    public void RepositionsWhenOverlayIsHidden()
    {
        Assert.True(OverlayRenderPolicy.ShouldReposition(
            isVisible: false,
            positionedGeneration: 7,
            generation: 7,
            Anchor,
            Anchor));
    }

    [Fact]
    public void KeepsPositionForTheSameVisibleGeneration()
    {
        Assert.False(OverlayRenderPolicy.ShouldReposition(
            isVisible: true,
            positionedGeneration: 7,
            generation: 7,
            Anchor,
            Anchor));
    }

    [Fact]
    public void RepositionsWhenAVisibleOverlayReceivesANewGeneration()
    {
        Assert.True(OverlayRenderPolicy.ShouldReposition(
            isVisible: true,
            positionedGeneration: 7,
            generation: 8,
            Anchor,
            Anchor));
    }

    [Fact]
    public void RepositionsWhenTheAnchorMovesDuringStabilization()
    {
        Assert.True(OverlayRenderPolicy.ShouldReposition(
            isVisible: true,
            positionedGeneration: 7,
            generation: 7,
            Anchor,
            Anchor with { X = 140 }));
    }
}
