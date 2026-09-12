using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Threading;
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
                PumpDispatcher(TimeSpan.FromMilliseconds(35));
                scaleBeforeCorrection = window.IndicatorScale.ScaleX;

                window.Render(VisibleState(InputState.English, IndicatorReasonCode.InputStateChanged));
                PumpDispatcher(TimeSpan.FromMilliseconds(35));
                scaleAfterCorrection = window.IndicatorScale.ScaleX;
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
        Assert.InRange(scaleBeforeCorrection ?? -1, 0.05, 0.95);
        Assert.True(
            scaleAfterCorrection >= scaleBeforeCorrection,
            $"Expansion reversed from {scaleBeforeCorrection:F3} to {scaleAfterCorrection:F3}.");
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
                PumpDispatcher(TimeSpan.FromMilliseconds(100));

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
    public void HiddenOverlayCompletesARepeatedAppearance()
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
                PumpDispatcher(TimeSpan.FromMilliseconds(120));
                window.Render(new IndicatorViewState(
                    1,
                    IndicatorPhase.Hidden,
                    InputState.Unknown,
                    null,
                    0,
                    IndicatorReasonCode.InputActivityDetected));

                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.InputIdleElapsed));
                PumpDispatcher(TimeSpan.FromMilliseconds(180));

                opacity = window.Opacity;
                scale = window.IndicatorScale.ScaleX;
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
        Assert.Equal(1, scale);
    }

    [Fact]
    public void DifferentTargetStartsCollapsedAndOnlyExpandsAtTheNewPosition()
    {
        double? opacity = null;
        double? baseScale = null;
        double? completedOpacity = null;
        double? completedScale = null;
        var transparentRenderFrames = 0;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                PumpDispatcher(TimeSpan.FromMilliseconds(100));

                EventHandler rendering = (_, _) =>
                {
                    if (window.Opacity == 0)
                    {
                        transparentRenderFrames++;
                    }
                };
                CompositionTarget.Rendering += rendering;
                try
                {
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
                    PumpDispatcher(TimeSpan.FromMilliseconds(100));
                    completedOpacity = window.Opacity;
                    completedScale = window.IndicatorScale.ScaleX;
                }
                finally
                {
                    CompositionTarget.Rendering -= rendering;
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
        Assert.Equal(0, opacity);
        Assert.Equal(0, baseScale);
        Assert.Equal(1, completedOpacity);
        Assert.Equal(1, completedScale);
        Assert.True(
            transparentRenderFrames >= 1,
            "No transparent frame reached WPF composition before the context moved.");
    }

    [Fact]
    public void TargetHandoffDoesNotMoveTheWindowBeforeTheOldSurfaceIsTransparent()
    {
        ScreenRect? beforeHandoff = null;
        ScreenRect? immediatelyAfterHandoff = null;
        ScreenRect? afterAppearance = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            IndicatorOverlayWindow? window = null;
            try
            {
                window = CreateWindow();
                window.Render(VisibleState(InputState.Chinese, IndicatorReasonCode.ContextEstablished));
                PumpDispatcher(TimeSpan.FromMilliseconds(100));
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

                PumpDispatcher(TimeSpan.FromMilliseconds(120));
                afterAppearance = window.PositionedAnchor;
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
        Assert.NotEqual(beforeHandoff, afterAppearance);
    }

    [Fact]
    public void SameTargetGenerationCorrectionDoesNotReplayTheAppearance()
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
                PumpDispatcher(TimeSpan.FromMilliseconds(100));

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
        Assert.Equal(1, baseScale);
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
                PumpDispatcher(TimeSpan.FromMilliseconds(100));

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
                        1,
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

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

}
