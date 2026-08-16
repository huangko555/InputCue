using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal sealed record RawInputContextObservation(
    nint ForegroundWindow,
    nint FocusWindow,
    int AutomationElementIdentity,
    TargetDescriptor Target,
    InputState InputState,
    InputStateEvidence InputStateEvidence,
    InputEvidence Evidence,
    UiAutomationCaretMethod UiAutomationCaretMethod,
    TextPattern2Status TextPattern2Status,
    double DurationMilliseconds)
{
    internal static RawInputContextObservation Failure(
        ProbeIssue issue,
        double durationMilliseconds) =>
        new(
            0,
            0,
            0,
            new TargetDescriptor(0, string.Empty, string.Empty, string.Empty, string.Empty),
            InputState.Unknown,
            InputStateEvidence.Unavailable,
            new InputEvidence(false, null, null, null, null, null, issue),
            UiAutomationCaretMethod.None,
            TextPattern2Status.NotAttempted,
            durationMilliseconds);

    internal ObservationFingerprint Fingerprint => new(
        ForegroundWindow,
        FocusWindow,
        AutomationElementIdentity,
        Target.ProcessId);
}

internal sealed record ObservationFingerprint(
    nint ForegroundWindow,
    nint FocusWindow,
    int AutomationElementIdentity,
    int ProcessId);
