using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class ContextReturnTrackerTests
{
    private static readonly DateTimeOffset Start = new(
        2026,
        8,
        19,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void SuppressesQuickReturnToASimilarEditorInTheSameNativeContext()
    {
        var tracker = new ContextReturnTracker(TimeSpan.FromMilliseconds(500));

        Assert.False(tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 1),
            Snapshot(1, Start, Eligibility.EditableCaret)));
        Assert.False(tracker.Observe(
            Observation("ControlType.Button", automationIdentity: 2),
            Snapshot(2, Start.AddMilliseconds(100), Eligibility.NoEditableFocus)));
        Assert.True(tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 3),
            Snapshot(3, Start.AddMilliseconds(300), Eligibility.EditableCaret)));
    }

    [Fact]
    public void DoesNotSuppressReturnAfterTheWindowExpires()
    {
        var tracker = new ContextReturnTracker(TimeSpan.FromMilliseconds(500));
        _ = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 1),
            Snapshot(1, Start, Eligibility.EditableCaret));
        _ = tracker.Observe(
            Observation("ControlType.Button", automationIdentity: 2),
            Snapshot(2, Start.AddMilliseconds(100), Eligibility.NoEditableFocus));

        var suppress = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 3),
            Snapshot(3, Start.AddMilliseconds(601), Eligibility.EditableCaret));

        Assert.False(suppress);
    }

    [Fact]
    public void DoesNotSuppressReturnToADifferentWindow()
    {
        var tracker = new ContextReturnTracker(TimeSpan.FromMilliseconds(500));
        _ = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 1),
            Snapshot(1, Start, Eligibility.EditableCaret));
        _ = tracker.Observe(
            Observation("ControlType.Button", automationIdentity: 2),
            Snapshot(2, Start.AddMilliseconds(100), Eligibility.NoEditableFocus));

        var suppress = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 3, foregroundWindow: 99),
            Snapshot(3, Start.AddMilliseconds(200), Eligibility.EditableCaret));

        Assert.False(suppress);
    }

    [Fact]
    public void ChangingWindowsWhileAwayInvalidatesTheCandidate()
    {
        var tracker = new ContextReturnTracker(TimeSpan.FromMilliseconds(500));
        _ = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 1),
            Snapshot(1, Start, Eligibility.EditableCaret));
        _ = tracker.Observe(
            Observation("ControlType.Button", automationIdentity: 2),
            Snapshot(2, Start.AddMilliseconds(100), Eligibility.NoEditableFocus));
        _ = tracker.Observe(
            Observation("ControlType.Button", automationIdentity: 3, foregroundWindow: 99),
            Snapshot(3, Start.AddMilliseconds(150), Eligibility.NoEditableFocus));

        var suppress = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 4),
            Snapshot(4, Start.AddMilliseconds(200), Eligibility.EditableCaret));

        Assert.False(suppress);
    }

    [Fact]
    public void DoesNotSuppressAnInputStateChange()
    {
        var tracker = new ContextReturnTracker(TimeSpan.FromMilliseconds(500));
        _ = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 1),
            Snapshot(1, Start, Eligibility.EditableCaret, InputState.English));
        _ = tracker.Observe(
            Observation("ControlType.Button", automationIdentity: 2),
            Snapshot(2, Start.AddMilliseconds(100), Eligibility.NoEditableFocus));

        var suppress = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 3),
            Snapshot(3, Start.AddMilliseconds(200), Eligibility.EditableCaret, InputState.Chinese));

        Assert.False(suppress);
    }

    [Fact]
    public void DirectEditorSwitchDoesNotSuppressReplay()
    {
        var tracker = new ContextReturnTracker(TimeSpan.FromMilliseconds(500));
        _ = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 1),
            Snapshot(1, Start, Eligibility.EditableCaret));

        var suppress = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 2),
            Snapshot(2, Start.AddMilliseconds(100), Eligibility.EditableCaret));

        Assert.False(suppress);
    }

    [Fact]
    public void ReturnToADisjointEditorDoesNotSuppressTheTargetSwitch()
    {
        var tracker = new ContextReturnTracker(TimeSpan.FromMilliseconds(500));
        _ = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 1, targetX: 100),
            Snapshot(1, Start, Eligibility.EditableCaret));
        _ = tracker.Observe(
            Observation("ControlType.Button", automationIdentity: 2),
            Snapshot(2, Start.AddMilliseconds(100), Eligibility.NoEditableFocus));

        var suppress = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 3, targetX: 700),
            Snapshot(3, Start.AddMilliseconds(200), Eligibility.EditableCaret));

        Assert.False(suppress);
    }

    [Fact]
    public void TemporaryMissingCaretInTheSameEditorDefersAndSuppressesReplay()
    {
        var tracker = new ContextReturnTracker(TimeSpan.FromMilliseconds(500));
        _ = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 1),
            Snapshot(1, Start, Eligibility.EditableCaret));

        var missingCaret = Observation("ControlType.Edit", automationIdentity: 2) with
        {
            Evidence = new InputEvidence(true, false, false, null, null, null),
        };
        Assert.False(tracker.Observe(
            missingCaret,
            Snapshot(1, Start.AddMilliseconds(100), Eligibility.PositionUnknown)));
        Assert.True(tracker.IsContextLossPending);

        var suppress = tracker.Observe(
            Observation("ControlType.Edit", automationIdentity: 3),
            Snapshot(1, Start.AddMilliseconds(200), Eligibility.EditableCaret));

        Assert.True(suppress);
    }

    private static RawInputContextObservation Observation(
        string controlType,
        int automationIdentity,
        nint foregroundWindow = 10,
        double targetX = 100) =>
        new(
            foregroundWindow,
            20,
            automationIdentity,
            new TargetDescriptor(30, "browser", controlType, string.Empty, "Chrome"),
            InputState.English,
            InputStateEvidence.Unavailable,
            new InputEvidence(
                controlType == "ControlType.Edit",
                false,
                false,
                controlType == "ControlType.Edit" ? new ScreenRect(100, 120, 2, 20) : null,
                null,
                null),
            UiAutomationCaretMethod.TextPattern,
            TextPattern2Status.PatternUnavailable,
            1)
        {
            TargetBounds = controlType == "ControlType.Edit"
                ? new ScreenRect(targetX, 100, 400, 40)
                : null,
        };

    private static InputContextSnapshot Snapshot(
        long generation,
        DateTimeOffset observedAt,
        Eligibility eligibility,
        InputState inputState = InputState.English) =>
        new(
            generation,
            observedAt,
            eligibility,
            inputState,
            eligibility is Eligibility.EditableCaret
                ? new ScreenRect(100, 120, 2, 20)
                : null,
            AnchorSource.UiAutomation,
            EvidenceGrade.Confirmed,
            eligibility is Eligibility.EditableCaret
                ? ReasonCode.EditableCaretConfirmed
                : ReasonCode.NoEditableFocus);
}
