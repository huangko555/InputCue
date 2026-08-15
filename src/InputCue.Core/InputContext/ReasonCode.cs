namespace InputCue.Core.InputContext;

public enum ReasonCode
{
    None,
    EditableCaretConfirmed,
    EditableSelection,
    ReadOnlySelection,
    NoEditableFocus,
    PositionUnavailable,
    StaleObservation,
    TimedOut,
    InsufficientPrivilege,
    ConflictingEvidence,
    SourceUnavailable,
}
