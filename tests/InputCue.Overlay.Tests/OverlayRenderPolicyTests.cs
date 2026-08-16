namespace InputCue.Overlay.Tests;

public sealed class OverlayRenderPolicyTests
{
    [Fact]
    public void RepositionsWhenOverlayIsHidden()
    {
        Assert.True(OverlayRenderPolicy.ShouldReposition(isVisible: false, positionedGeneration: 7, generation: 7));
    }

    [Fact]
    public void KeepsPositionForTheSameVisibleGeneration()
    {
        Assert.False(OverlayRenderPolicy.ShouldReposition(isVisible: true, positionedGeneration: 7, generation: 7));
    }

    [Fact]
    public void RepositionsWhenAVisibleOverlayReceivesANewGeneration()
    {
        Assert.True(OverlayRenderPolicy.ShouldReposition(isVisible: true, positionedGeneration: 7, generation: 8));
    }
}
