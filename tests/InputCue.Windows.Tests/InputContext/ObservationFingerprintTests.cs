using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class ObservationFingerprintTests
{
    [Fact]
    public void FingerprintChangesWhenSelectionGeometryChanges()
    {
        var first = Observation(selectionIdentity: 101);
        var second = Observation(selectionIdentity: 202);

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void FingerprintDoesNotChangeWhenOnlyInputStateChanges()
    {
        var first = Observation(selectionIdentity: 101, InputState.Chinese);
        var second = Observation(selectionIdentity: 101, InputState.English);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    private static RawInputContextObservation Observation(
        int selectionIdentity,
        InputState inputState = InputState.Unknown) =>
        new(
            1,
            2,
            3,
            selectionIdentity,
            new TargetDescriptor(4, "browser", "Document", "", "Chrome"),
            inputState,
            new InputEvidence(false, true, true, null, null, null),
            UiAutomationCaretMethod.None,
            TextPattern2Status.NotAttempted,
            1);
}
