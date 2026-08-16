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
    public void FingerprintChangesWhenSelectionStarts()
    {
        var first = Observation(hasSelection: false);
        var second = Observation(hasSelection: true);

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
        double caretX = 100) =>
        new(
            1,
            2,
            3,
            new TargetDescriptor(4, "browser", "ControlType.Edit", "", "Chrome"),
            inputState,
            InputStateEvidence.Unavailable,
            new InputEvidence(
                true,
                false,
                hasSelection,
                new ScreenRect(caretX, 120, 2, 20),
                null,
                null),
            UiAutomationCaretMethod.TextPattern,
            TextPattern2Status.NotAttempted,
            1);
}
