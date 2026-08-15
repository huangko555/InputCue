namespace InputCue.Core.InputContext;

public enum Eligibility
{
    Unknown,
    EditableCaret,
    EditableSelection,
    ReadOnlySelection,
    NoEditableFocus,
    PositionUnknown,
}
