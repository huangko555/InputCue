using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class ObservationFingerprintTests
{
    [Fact]
    public void FingerprintDoesNotChangeWhenSelectionAnchorMoves()
    {
        var first = Observation(caretX: 100);
        var second = Observation(caretX: 200);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void FingerprintDoesNotChangeWhenSelectionStarts()
    {
        var first = Observation(hasSelection: false);
        var second = Observation(hasSelection: true);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void FingerprintDoesNotChangeWhenTargetFactsTemporarilyDegrade()
    {
        var confirmed = Observation();
        var degraded = Observation(
            hasEditableFocus: false,
            isReadOnly: null,
            issue: ProbeIssue.SourceUnavailable);

        Assert.Equal(confirmed.Fingerprint, degraded.Fingerprint);
    }

    [Fact]
    public void FingerprintChangesWhenAutomationTargetChanges()
    {
        var first = Observation(automationElementIdentity: 3);
        var second = Observation(automationElementIdentity: 5);

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void FingerprintDoesNotChangeWhenOnlyInputStateChanges()
    {
        var first = Observation(inputState: InputState.Chinese);
        var second = Observation(inputState: InputState.English);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    private static RawInputContextObservation Observation(
        bool hasSelection = true,
        InputState inputState = InputState.Unknown,
        double caretX = 100,
        bool hasEditableFocus = true,
        bool? isReadOnly = false,
        ProbeIssue issue = ProbeIssue.None,
        int automationElementIdentity = 3) =>
        new(
            1,
            2,
            automationElementIdentity,
            new TargetDescriptor(4, "browser", "ControlType.Edit", "", "Chrome"),
            inputState,
            InputStateEvidence.Unavailable,
            new InputEvidence(
                hasEditableFocus,
                isReadOnly,
                hasSelection,
                new ScreenRect(caretX, 120, 2, 20),
                null,
                null,
                issue),
            UiAutomationCaretMethod.TextPattern,
            TextPattern2Status.NotAttempted,
            1);
}
