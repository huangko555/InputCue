namespace InputCue.Overlay.Tests;

using InputCue.Core.Indicator;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;

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

    [Fact]
    public void MovingWithinTheSameContextRepositionsWithoutChangingZOrder()
    {
        Assert.True(OverlayRenderPolicy.ShouldReposition(
            isVisible: true,
            positionedGeneration: 7,
            generation: 7,
            Anchor,
            Anchor with { X = 140 }));
        Assert.True(OverlayRenderPolicy.ShouldPreserveZOrder(
            isVisible: true,
            targetChanged: false));
    }

    [Fact]
    public void ReactivatingTheSameContextRepositionsAndRestoresNormalBandFront()
    {
        Assert.True(OverlayRenderPolicy.ShouldReposition(
            isVisible: true,
            positionedGeneration: 7,
            generation: 7,
            Anchor,
            Anchor,
            contextActivated: true));
        Assert.False(OverlayRenderPolicy.ShouldPreserveZOrder(
            isVisible: true,
            targetChanged: false,
            contextActivated: true));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void PreservesZOrderOnlyWhileTrackingTheSameVisibleTarget(
        bool isVisible,
        bool targetChanged,
        bool expected)
    {
        Assert.Equal(
            expected,
            OverlayRenderPolicy.ShouldPreserveZOrder(
                isVisible,
                targetChanged));
    }

    [Fact]
    public void FlipRunsOnlyForAVisibleInputStateChange()
    {
        Assert.True(OverlayRenderPolicy.ShouldAnimateTransition(
            IndicatorTransitionAnimation.Flip,
            isVisible: true,
            InputState.Chinese,
            InputState.English,
            IndicatorReasonCode.InputStateChanged));
    }

    [Theory]
    [InlineData(IndicatorTransitionAnimation.None, true, InputState.Chinese, InputState.English, IndicatorReasonCode.InputStateChanged)]
    [InlineData(IndicatorTransitionAnimation.Flip, false, InputState.Chinese, InputState.English, IndicatorReasonCode.InputStateChanged)]
    [InlineData(IndicatorTransitionAnimation.Flip, true, null, InputState.English, IndicatorReasonCode.InputStateChanged)]
    [InlineData(IndicatorTransitionAnimation.Flip, true, InputState.English, InputState.English, IndicatorReasonCode.InputStateChanged)]
    [InlineData(IndicatorTransitionAnimation.Flip, true, InputState.Chinese, InputState.English, IndicatorReasonCode.ContextEstablished)]
    public void FlipDoesNotRunOutsideAVisibleStateChange(
        IndicatorTransitionAnimation animation,
        bool isVisible,
        InputState? renderedState,
        InputState nextState,
        IndicatorReasonCode reasonCode)
    {
        Assert.False(OverlayRenderPolicy.ShouldAnimateTransition(
            animation,
            isVisible,
            renderedState,
            nextState,
            reasonCode));
    }

    [Theory]
    [InlineData(IndicatorTransitionAnimation.Flip, false, true)]
    [InlineData(IndicatorTransitionAnimation.Flip, true, false)]
    [InlineData(IndicatorTransitionAnimation.None, false, false)]
    public void FlipAppearanceRunsOnlyWhenAConfiguredOverlayIsHidden(
        IndicatorTransitionAnimation animation,
        bool isVisible,
        bool expected)
    {
        Assert.Equal(
            expected,
            OverlayRenderPolicy.ShouldAnimateAppearance(animation, isVisible));
    }
}
