using System.Runtime.ExceptionServices;
using System.Windows.Media;
using InputCue.Core.Indicator;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;

namespace InputCue.Overlay.Tests;

public sealed class IndicatorOverlayWindowAnimationTests
{
    private static readonly ScreenRect Anchor = new(100, 100, 2, 20);

    [Fact]
    public void FlipAppearanceKeepsThePreAnimationFrameCollapsed()
    {
        double? baseScale = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = new IndicatorOverlayWindow();
                window.Configure(
                    IndicatorStyle.Default,
                    IndicatorPlacement.Top,
                    0,
                    4,
                    36,
                    22,
                    IndicatorTransitionAnimation.Flip,
                    "E5534B",
                    "2F7FD6",
                    "D99000",
                    "2F9E68");
                window.Render(new IndicatorViewState(
                    1,
                    IndicatorPhase.Visible,
                    InputState.Chinese,
                    Anchor,
                    1,
                    IndicatorReasonCode.ContextEstablished));

                baseScale = (double)window.IndicatorScale.GetAnimationBaseValue(
                    ScaleTransform.ScaleXProperty);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA animation test timed out.");
        failure?.Throw();
        Assert.Equal(0, baseScale);
    }

    [Fact]
    public void StateCorrectionDuringAppearanceDoesNotReverseTheExpansion()
    {
        double? scaleBeforeCorrection = null;
        double? scaleAfterCorrection = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                AdvanceAppearanceFrames(window, 2);
                scaleBeforeCorrection = (double)window.IndicatorScale.GetAnimationBaseValue(
                    ScaleTransform.ScaleXProperty);

                window.Render(VisibleState(InputState.English, IndicatorReasonCode.InputStateChanged));
                scaleAfterCorrection = (double)window.IndicatorScale.GetAnimationBaseValue(
                    ScaleTransform.ScaleXProperty);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA animation test timed out.");
        failure?.Throw();
        Assert.Equal(0, scaleBeforeCorrection);
        Assert.Equal(scaleBeforeCorrection, scaleAfterCorrection);
    }

    [Fact]
    public void HidingAnInitializedOverlayKeepsItsWindowComposedAndTransparent()
    {
        bool? windowIsVisible = null;
        double? opacity = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                AdvanceAppearanceFrames(window, 2);

                window.HideIndicator();

                windowIsVisible = window.IsVisible;
                opacity = window.Opacity;
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA animation test timed out.");
        failure?.Throw();
        Assert.True(windowIsVisible);
        Assert.Equal(0, opacity);
    }

    [Fact]
    public void HiddenOverlayStartsARepeatedAppearanceFromCollapsed()
    {
        double? opacity = null;
        double? scale = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                AdvanceAppearanceFrames(window, 2);
                window.Render(new IndicatorViewState(
                    1,
                    IndicatorPhase.Hidden,
                    InputState.Unknown,
                    null,
                    0,
                    IndicatorReasonCode.InputActivityDetected));

                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.InputIdleElapsed));
                AdvanceAppearanceFrames(window, 2);

                opacity = window.Opacity;
                scale = (double)window.IndicatorScale.GetAnimationBaseValue(
                    ScaleTransform.ScaleXProperty);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA animation test timed out.");
        failure?.Throw();
        Assert.Equal(1, opacity);
        Assert.Equal(0, scale);
    }

    [Fact]
    public void DifferentTargetStartsCollapsedAndOnlyExpandsAtTheNewPosition()
    {
        double? opacity = null;
        double? baseScale = null;
        double? completedOpacity = null;
        double? completedBaseScale = null;
        ScreenRect? beforeHandoff = null;
        ScreenRect? afterFirstFrame = null;
        ScreenRect? afterSecondFrame = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                AdvanceAppearanceFrames(window, 2);
                beforeHandoff = window.PositionedAnchor;

                window.Render(
                    new IndicatorViewState(
                        2,
                        IndicatorPhase.Visible,
                        InputState.Chinese,
                        Anchor with { X = 180 },
                        1,
                        IndicatorReasonCode.ContextEstablished),
                    targetChanged: true);

                opacity = window.Opacity;
                baseScale = (double)window.IndicatorScale.GetAnimationBaseValue(
                    ScaleTransform.ScaleXProperty);
                AdvanceAppearanceFrames(window, 1);
                afterFirstFrame = window.PositionedAnchor;
                AdvanceAppearanceFrames(window, 1);
                afterSecondFrame = window.PositionedAnchor;
                Assert.Equal(0, window.Opacity);
                AdvanceAppearanceFrames(window, 1);
                completedOpacity = window.Opacity;
                completedBaseScale = (double)window.IndicatorScale.GetAnimationBaseValue(
                    ScaleTransform.ScaleXProperty);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA animation test timed out.");
        failure?.Throw();
        Assert.Equal(0, opacity);
        Assert.Equal(0, baseScale);
        Assert.Equal(beforeHandoff, afterFirstFrame);
        Assert.NotEqual(beforeHandoff, afterSecondFrame);
        Assert.Equal(1, completedOpacity);
        Assert.Equal(0, completedBaseScale);
    }

    [Fact]
    public void TargetHandoffDoesNotMoveTheWindowBeforeTheOldSurfaceIsTransparent()
    {
        ScreenRect? beforeHandoff = null;
        ScreenRect? immediatelyAfterHandoff = null;
        ScreenRect? afterTransparentFrame = null;
        ScreenRect? afterAppearance = null;
        double? opacityAfterTargetMove = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                AdvanceAppearanceFrames(window, 2);
                beforeHandoff = window.PositionedAnchor;

                window.Render(
                    new IndicatorViewState(
                        2,
                        IndicatorPhase.Visible,
                        InputState.Chinese,
                        Anchor with { X = 420 },
                        1,
                        IndicatorReasonCode.ContextEstablished),
                    targetChanged: true);
                immediatelyAfterHandoff = window.PositionedAnchor;

                AdvanceAppearanceFrames(window, 1);
                afterTransparentFrame = window.PositionedAnchor;
                AdvanceAppearanceFrames(window, 1);
                afterAppearance = window.PositionedAnchor;
                opacityAfterTargetMove = window.Opacity;
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA animation test timed out.");
        failure?.Throw();
        Assert.Equal(beforeHandoff, immediatelyAfterHandoff);
        Assert.Equal(beforeHandoff, afterTransparentFrame);
        Assert.NotEqual(beforeHandoff, afterAppearance);
        Assert.Equal(0, opacityAfterTargetMove);
    }

    [Fact]
    public void SameTargetGenerationCorrectionDoesNotReprimeTheAppearance()
    {
        double? opacity = null;
        double? baseScale = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                AdvanceAppearanceFrames(window, 2);

                window.Render(
                    new IndicatorViewState(
                        2,
                        IndicatorPhase.Visible,
                        InputState.Chinese,
                        Anchor with { X = 180 },
                        1,
                        IndicatorReasonCode.ContextEstablished),
                    targetChanged: false);

                opacity = window.Opacity;
                baseScale = (double)window.IndicatorScale.GetAnimationBaseValue(
                    ScaleTransform.ScaleXProperty);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA animation test timed out.");
        failure?.Throw();
        Assert.Equal(1, opacity);
        Assert.Equal(0, baseScale);
    }

    [Fact]
    public void OneHundredCaretMovesInTheSameTargetNeverRestartAppearance()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                AdvanceAppearanceFrames(window, 2);

                for (var index = 0; index < 100; index++)
                {
                    window.Render(
                        new IndicatorViewState(
                            1,
                            IndicatorPhase.Visible,
                            InputState.Chinese,
                            Anchor with { X = 100 + index * 3 },
                            1,
                            IndicatorReasonCode.ContextEstablished),
                        targetChanged: true);

                    Assert.Equal(1, window.Opacity);
                    Assert.Equal(
                        0,
                        (double)window.IndicatorScale.GetAnimationBaseValue(
                            ScaleTransform.ScaleXProperty));
                }
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA animation test timed out.");
        failure?.Throw();
    }

    [Fact]
    public void AppearanceDurationIsHalfOfTheFullFlipDuration()
    {
        Assert.Equal(
            IndicatorOverlayWindow.FlipCollapseMilliseconds +
            IndicatorOverlayWindow.FlipExpandMilliseconds,
            IndicatorOverlayWindow.FlipAppearanceMilliseconds * 2);
    }

    private static IndicatorOverlayWindow CreateWindow()
    {
        var window = new IndicatorOverlayWindow();
        window.Configure(
            IndicatorStyle.Default,
            IndicatorPlacement.Top,
            0,
            4,
            36,
            22,
            IndicatorTransitionAnimation.Flip,
            "E5534B",
            "2F7FD6",
            "D99000",
            "2F9E68");
        return window;
    }

    private static IndicatorViewState VisibleState(
        InputState state,
        IndicatorReasonCode reasonCode) =>
        new(1, IndicatorPhase.Visible, state, Anchor, 1, reasonCode);

    private static void AdvanceAppearanceFrames(
        IndicatorOverlayWindow window,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            window.AdvanceAppearanceFrame();
        }
    }
}
