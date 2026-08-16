namespace InputCue.Core.InputContext;

public enum ProbeIssue
{
    None,
    TimedOut,
    InsufficientPrivilege,
    ConflictingEvidence,
    FocusedProcessMismatch,
    FocusWindowProcessMismatch,
    ObservationIdentityChanged,
    InputStateEvidenceChanged,
    SourceUnavailable,
}
