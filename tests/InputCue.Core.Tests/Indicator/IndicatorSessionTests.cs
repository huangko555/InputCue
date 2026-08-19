using InputCue.Core.Indicator;
using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.Indicator;

public sealed class IndicatorSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly ScreenRect Caret = new(100, 120, 2, 20);
    private static readonly IndicatorSessionOptions Transient = new(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(200));

    [Fact]
    public void StartsHidden()
    {
        var session = new IndicatorSession(Transient);

        Assert.Equal(IndicatorPhase.Hidden, session.Current.Phase);
        Assert.Equal(IndicatorReasonCode.Initial, session.Current.ReasonCode);
        Assert.False(session.Current.IsVisible);
    }

    [Fact]
    public void EligibleKnownInputStateShowsAtCaret()
    {
        var session = new IndicatorSession(Transient);

        var state = session.Observe(Snapshot(1, Start));

        Assert.Equal(IndicatorPhase.Visible, state.Phase);
        Assert.Equal(InputState.English, state.InputState);
        Assert.Equal(Caret, state.Anchor);
        Assert.Equal(1, state.Opacity);
        Assert.Equal(IndicatorReasonCode.ContextEstablished, state.ReasonCode);
    }

    [Theory]
    [InlineData(Eligibility.ReadOnlySelection, IndicatorReasonCode.ContextIneligible)]
    [InlineData(Eligibility.NoEditableFocus, IndicatorReasonCode.ContextIneligible)]
    [InlineData(Eligibility.Unknown, IndicatorReasonCode.ContextIneligible)]
    [InlineData(Eligibility.PositionUnknown, IndicatorReasonCode.PositionUnavailable)]
    public void IneligibleContextHides(
        Eligibility eligibility,
        IndicatorReasonCode expectedReason)
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start));

        var state = session.Observe(Snapshot(2, Start.AddMilliseconds(1), eligibility));

        Assert.Equal(IndicatorPhase.Hidden, state.Phase);
        Assert.Equal(InputState.Unknown, state.InputState);
        Assert.Null(state.Anchor);
        Assert.Equal(expectedReason, state.ReasonCode);
    }

    [Fact]
    public void EditableSelectionWithKnownInputStateShowsAtSafeAnchor()
    {
        var session = new IndicatorSession(Transient);

        var state = session.Observe(Snapshot(
            1,
            Start,
            Eligibility.EditableSelection));

        Assert.Equal(IndicatorPhase.Visible, state.Phase);
        Assert.Equal(Caret, state.Anchor);
        Assert.Equal(IndicatorReasonCode.ContextEstablished, state.ReasonCode);
    }

    [Fact]
    public void RepeatedEditableSelectionKeepsThePresentedAnchorAndDeadline()
    {
        var movedCaret = new ScreenRect(130, 120, 2, 20);
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(
            1,
            Start,
            Eligibility.EditableSelection));

        _ = session.Observe(Snapshot(
            1,
            Start.AddMilliseconds(900),
            Eligibility.EditableSelection,
            anchor: movedCaret));
        var state = session.Advance(Start.AddMilliseconds(1050));

        Assert.Equal(IndicatorPhase.Fading, state.Phase);
        Assert.Equal(0.75, state.Opacity, 3);
        Assert.Equal(Caret, state.Anchor);
    }

    [Fact]
    public void PositionStabilizationMovesAnchorWithoutExtendingDeadline()
    {
        var movedCaret = new ScreenRect(130, 140, 2, 20);
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start));

        var repositioned = session.Observe(
            Snapshot(1, Start.AddMilliseconds(150), anchor: movedCaret),
            receivedAt: null,
            refreshAnchor: true);
        var state = session.Advance(Start.AddMilliseconds(1050));

        Assert.Equal(movedCaret, repositioned.Anchor);
        Assert.Equal(IndicatorPhase.Fading, state.Phase);
        Assert.Equal(0.75, state.Opacity, 3);
    }

    [Fact]
    public void UnknownInputStateNeverShows()
    {
        var session = new IndicatorSession(Transient);

        var state = session.Observe(Snapshot(1, Start, inputState: InputState.Unknown));

        Assert.Equal(IndicatorPhase.Hidden, state.Phase);
        Assert.Equal(IndicatorReasonCode.InputStateUnknown, state.ReasonCode);
    }

    [Fact]
    public void MissingAnchorNeverShows()
    {
        var session = new IndicatorSession(Transient);

        var state = session.Observe(Snapshot(1, Start, includeAnchor: false));

        Assert.Equal(IndicatorPhase.Hidden, state.Phase);
        Assert.Equal(IndicatorReasonCode.PositionUnavailable, state.ReasonCode);
    }

    [Fact]
    public void RepeatedObservationDoesNotExtendDisplayDuration()
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start));

        _ = session.Observe(Snapshot(1, Start.AddMilliseconds(900)));
        var state = session.Advance(Start.AddMilliseconds(1050));

        Assert.Equal(IndicatorPhase.Fading, state.Phase);
        Assert.Equal(0.75, state.Opacity, 3);
    }

    [Fact]
    public void TextInputHidesTheVisibleIndicatorImmediately()
    {
        var session = new IndicatorSession(Transient with
        {
            MinimumDisplayDuration = TimeSpan.Zero,
        });
        _ = session.Observe(Snapshot(1, Start));

        var state = session.ObserveInputActivity(Start.AddMilliseconds(100));

        Assert.Equal(IndicatorPhase.Hidden, state.Phase);
        Assert.Equal(IndicatorReasonCode.InputActivityDetected, state.ReasonCode);
    }

    [Fact]
    public void TextInputWaitsForTheMinimumDisplayDuration()
    {
        var session = new IndicatorSession(Transient with
        {
            MinimumDisplayDuration = TimeSpan.FromMilliseconds(300),
        });
        _ = session.Observe(Snapshot(1, Start));

        var pending = session.ObserveInputActivity(Start.AddMilliseconds(100));
        var beforeMinimum = session.Advance(Start.AddMilliseconds(299));
        var atMinimum = session.Advance(Start.AddMilliseconds(300));

        Assert.Equal(IndicatorPhase.Visible, pending.Phase);
        Assert.Equal(IndicatorPhase.Visible, beforeMinimum.Phase);
        Assert.Equal(IndicatorPhase.Hidden, atMinimum.Phase);
        Assert.Equal(IndicatorReasonCode.InputActivityDetected, atMinimum.ReasonCode);
    }

    [Fact]
    public void MinimumDisplayDurationStartsWhenTheObservationIsPresented()
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start), Start.AddMilliseconds(500));

        var pending = session.ObserveInputActivity(Start.AddMilliseconds(520));
        var beforeMinimum = session.Advance(Start.AddMilliseconds(799));
        var atMinimum = session.Advance(Start.AddMilliseconds(800));

        Assert.Equal(IndicatorPhase.Visible, pending.Phase);
        Assert.Equal(IndicatorPhase.Visible, beforeMinimum.Phase);
        Assert.Equal(IndicatorPhase.Hidden, atMinimum.Phase);
    }

    [Fact]
    public void RecentInputSuppressesContextReplayFromAChangedGeneration()
    {
        var session = new IndicatorSession(Transient with
        {
            MinimumDisplayDuration = TimeSpan.Zero,
            ContextReplaySuppressionDuration = TimeSpan.FromMilliseconds(1500),
        });
        _ = session.Observe(Snapshot(1, Start));
        _ = session.ObserveInputActivity(Start.AddMilliseconds(100));

        var state = session.Observe(Snapshot(2, Start.AddMilliseconds(200)));

        Assert.Equal(IndicatorPhase.Hidden, state.Phase);
        Assert.Equal(IndicatorReasonCode.InputActivityDetected, state.ReasonCode);
    }

    [Fact]
    public void InputWhileHiddenRefreshesContextReplaySuppression()
    {
        var session = new IndicatorSession(Transient with
        {
            MinimumDisplayDuration = TimeSpan.Zero,
            ContextReplaySuppressionDuration = TimeSpan.FromMilliseconds(1500),
        });
        _ = session.Observe(Snapshot(1, Start));
        _ = session.ObserveInputActivity(Start.AddMilliseconds(100));
        _ = session.ObserveInputActivity(Start.AddMilliseconds(1000));

        var state = session.Observe(Snapshot(2, Start.AddMilliseconds(2000)));

        Assert.Equal(IndicatorPhase.Hidden, state.Phase);
    }

    [Fact]
    public void InputStateChangeBypassesContextReplaySuppression()
    {
        var session = new IndicatorSession(Transient with
        {
            MinimumDisplayDuration = TimeSpan.Zero,
            ContextReplaySuppressionDuration = TimeSpan.FromMilliseconds(1500),
        });
        _ = session.Observe(Snapshot(1, Start));
        _ = session.ObserveInputActivity(Start.AddMilliseconds(100));

        var state = session.Observe(Snapshot(
            2,
            Start.AddMilliseconds(200),
            inputState: InputState.Chinese));

        Assert.Equal(IndicatorPhase.Visible, state.Phase);
        Assert.Equal(IndicatorReasonCode.InputStateChanged, state.ReasonCode);
    }

    [Fact]
    public void ContextReplayIsAllowedAfterSuppressionExpires()
    {
        var session = new IndicatorSession(Transient with
        {
            MinimumDisplayDuration = TimeSpan.Zero,
            ContextReplaySuppressionDuration = TimeSpan.FromMilliseconds(1500),
        });
        _ = session.Observe(Snapshot(1, Start));
        _ = session.ObserveInputActivity(Start.AddMilliseconds(100));

        var state = session.Observe(Snapshot(2, Start.AddMilliseconds(1600)));

        Assert.Equal(IndicatorPhase.Visible, state.Phase);
        Assert.Equal(IndicatorReasonCode.ContextEstablished, state.ReasonCode);
    }

    [Fact]
    public void LikelyContextReturnUpdatesContextWithoutReplayingIndicator()
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start));
        _ = session.Observe(Snapshot(
            2,
            Start.AddMilliseconds(100),
            Eligibility.NoEditableFocus));

        var returned = session.Observe(
            Snapshot(3, Start.AddMilliseconds(300)),
            receivedAt: null,
            suppressContextReplay: true);
        var genuineSwitch = session.Observe(Snapshot(4, Start.AddMilliseconds(600)));

        Assert.Equal(IndicatorPhase.Hidden, returned.Phase);
        Assert.Equal(IndicatorPhase.Visible, genuineSwitch.Phase);
        Assert.Equal(IndicatorReasonCode.ContextEstablished, genuineSwitch.ReasonCode);
    }

    [Fact]
    public void LikelyContextReturnIgnoresUnknownStateFromTemporaryFocusLoss()
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(
            1,
            Start,
            inputState: InputState.Chinese));
        _ = session.Observe(Snapshot(
            2,
            Start.AddMilliseconds(100),
            Eligibility.NoEditableFocus,
            inputState: InputState.Unknown));

        var returned = session.Observe(
            Snapshot(
                3,
                Start.AddMilliseconds(300),
                inputState: InputState.Chinese),
            receivedAt: null,
            suppressContextReplay: true);

        Assert.Equal(IndicatorPhase.Hidden, returned.Phase);
        Assert.Equal(IndicatorReasonCode.ContextIneligible, returned.ReasonCode);
    }

    [Fact]
    public void AlwaysVisibleModeReplaysALikelyContextReturn()
    {
        var session = new IndicatorSession(Transient with { AlwaysVisible = true });
        _ = session.Observe(Snapshot(1, Start));
        _ = session.Observe(Snapshot(
            2,
            Start.AddMilliseconds(100),
            Eligibility.NoEditableFocus));

        var returned = session.Observe(
            Snapshot(3, Start.AddMilliseconds(300)),
            receivedAt: null,
            suppressContextReplay: true);

        Assert.Equal(IndicatorPhase.Visible, returned.Phase);
    }

    [Fact]
    public void InputStateChangeReplaysWithinSameGeneration()
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start));

        var changed = session.Observe(Snapshot(
            1,
            Start.AddMilliseconds(900),
            inputState: InputState.Chinese));
        var later = session.Advance(Start.AddMilliseconds(1100));

        Assert.Equal(IndicatorReasonCode.InputStateChanged, changed.ReasonCode);
        Assert.Equal(InputState.Chinese, changed.InputState);
        Assert.Equal(IndicatorPhase.Visible, later.Phase);
    }

    [Fact]
    public void InputStateChangeWithSameTimestampIsAcceptedInArrivalOrder()
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start));

        var changed = session.Observe(Snapshot(
            1,
            Start,
            inputState: InputState.Chinese));

        Assert.Equal(InputState.Chinese, changed.InputState);
        Assert.Equal(IndicatorReasonCode.InputStateChanged, changed.ReasonCode);
    }

    [Fact]
    public void NewGenerationReplaysUnchangedInputState()
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start));

        var changed = session.Observe(Snapshot(2, Start.AddMilliseconds(900)));
        var later = session.Advance(Start.AddMilliseconds(1100));

        Assert.Equal(IndicatorReasonCode.ContextEstablished, changed.ReasonCode);
        Assert.Equal(IndicatorPhase.Visible, later.Phase);
    }

    [Fact]
    public void OlderGenerationCannotChangeCurrentState()
    {
        var session = new IndicatorSession(Transient);
        var current = session.Observe(Snapshot(
            2,
            Start,
            inputState: InputState.Chinese));

        var result = session.Observe(Snapshot(
            1,
            Start.AddSeconds(1),
            Eligibility.NoEditableFocus));

        Assert.Equal(current, result);
    }

    [Fact]
    public void OlderObservationWithinGenerationCannotChangeCurrentState()
    {
        var session = new IndicatorSession(Transient);
        var current = session.Observe(Snapshot(
            1,
            Start.AddSeconds(1),
            inputState: InputState.Chinese));

        var result = session.Observe(Snapshot(
            1,
            Start,
            Eligibility.NoEditableFocus));

        Assert.Equal(current, result);
    }

    [Fact]
    public void AdvanceFadesThenHides()
    {
        var session = new IndicatorSession(Transient);
        _ = session.Observe(Snapshot(1, Start));

        var fading = session.Advance(Start.AddMilliseconds(1100));
        var hidden = session.Advance(Start.AddMilliseconds(1200));

        Assert.Equal(IndicatorPhase.Fading, fading.Phase);
        Assert.Equal(0.5, fading.Opacity, 3);
        Assert.Equal(IndicatorReasonCode.DisplayDurationElapsed, fading.ReasonCode);
        Assert.Equal(IndicatorPhase.Hidden, hidden.Phase);
        Assert.Equal(IndicatorReasonCode.FadeCompleted, hidden.ReasonCode);
        Assert.Null(hidden.Anchor);
    }

    [Fact]
    public void AlwaysVisibleModeDoesNotExpire()
    {
        var session = new IndicatorSession(Transient with { AlwaysVisible = true });
        _ = session.Observe(Snapshot(1, Start));

        var state = session.Advance(Start.AddHours(1));

        Assert.Equal(IndicatorPhase.Visible, state.Phase);
        Assert.Equal(1, state.Opacity);
    }

    [Fact]
    public void ZeroFadeDurationHidesAtDisplayDeadline()
    {
        var session = new IndicatorSession(Transient with { FadeDuration = TimeSpan.Zero });
        _ = session.Observe(Snapshot(1, Start));

        var state = session.Advance(Start.AddSeconds(1));

        Assert.Equal(IndicatorPhase.Hidden, state.Phase);
    }

    [Fact]
    public void NegativeDurationsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IndicatorSession(Transient with { DisplayDuration = TimeSpan.FromMilliseconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IndicatorSession(Transient with { FadeDuration = TimeSpan.FromMilliseconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IndicatorSession(Transient with { MinimumDisplayDuration = TimeSpan.FromMilliseconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IndicatorSession(Transient with { ContextReplaySuppressionDuration = TimeSpan.FromMilliseconds(-1) }));
    }

    [Theory]
    [InlineData(17)]
    [InlineData(117)]
    [InlineData(1017)]
    public void RandomEventSequencesPreserveViewStateInvariants(int seed)
    {
        var random = new Random(seed);
        var session = new IndicatorSession(Transient);
        var now = Start;
        long latestGeneration = 0;

        for (var index = 0; index < 500; index++)
        {
            if (random.Next(4) == 0)
            {
                now = now.AddMilliseconds(random.Next(0, 300));
                _ = session.Advance(now);
            }
            else
            {
                var isStale = latestGeneration > 1 && random.Next(5) == 0;
                var generation = isStale
                    ? random.NextInt64(1, latestGeneration)
                    : latestGeneration + random.Next(0, 2);
                generation = Math.Max(1, generation);
                var eligibility = (Eligibility)random.Next(Enum.GetValues<Eligibility>().Length);
                var inputState = (InputState)random.Next(Enum.GetValues<InputState>().Length);
                var before = session.Current;

                var state = session.Observe(Snapshot(
                    generation,
                    now,
                    eligibility,
                    inputState,
                    includeAnchor: random.Next(4) != 0));

                if (isStale)
                {
                    Assert.Equal(before, state);
                }
                else
                {
                    latestGeneration = Math.Max(latestGeneration, generation);
                }
            }

            AssertViewStateInvariants(session.Current);
        }
    }

    private static void AssertViewStateInvariants(IndicatorViewState state)
    {
        Assert.Equal(state.Phase is not IndicatorPhase.Hidden, state.IsVisible);
        Assert.InRange(state.Opacity, 0, 1);

        if (state.Phase is IndicatorPhase.Hidden)
        {
            Assert.Equal(0, state.Opacity);
            Assert.Equal(InputState.Unknown, state.InputState);
            Assert.Null(state.Anchor);
            return;
        }

        Assert.NotEqual(InputState.Unknown, state.InputState);
        Assert.True(state.Anchor is { IsUsable: true });
        if (state.Phase is IndicatorPhase.Visible)
        {
            Assert.Equal(1, state.Opacity);
        }
    }

    private static InputContextSnapshot Snapshot(
        long generation,
        DateTimeOffset observedAt,
        Eligibility eligibility = Eligibility.EditableCaret,
        InputState inputState = InputState.English,
        bool includeAnchor = true,
        ScreenRect? anchor = null) =>
        new(
            generation,
            observedAt,
            eligibility,
            inputState,
            includeAnchor ? anchor ?? Caret : null,
            AnchorSource.UiAutomation,
            EvidenceGrade.Confirmed,
            eligibility is Eligibility.EditableSelection
                ? ReasonCode.EditableSelection
                : ReasonCode.EditableCaretConfirmed);
}
