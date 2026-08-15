using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal sealed record RawInputContextObservation(
    nint ForegroundWindow,
    nint FocusWindow,
    int AutomationElementIdentity,
    int SelectionIdentity,
    TargetDescriptor Target,
    InputEvidence Evidence,
    double DurationMilliseconds)
{
    internal ObservationFingerprint Fingerprint => new(
        ForegroundWindow,
        FocusWindow,
        AutomationElementIdentity,
        SelectionIdentity,
        Target.ProcessId,
        Evidence.HasEditableFocus,
        Evidence.IsReadOnly,
        Evidence.HasSelection,
        Evidence.Issue);
}

internal sealed record ObservationFingerprint(
    nint ForegroundWindow,
    nint FocusWindow,
    int AutomationElementIdentity,
    int SelectionIdentity,
    int ProcessId,
    bool HasEditableFocus,
    bool? IsReadOnly,
    bool? HasSelection,
    ProbeIssue Issue);
