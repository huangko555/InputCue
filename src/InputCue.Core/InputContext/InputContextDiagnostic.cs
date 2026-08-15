namespace InputCue.Core.InputContext;

public sealed record InputContextDiagnostic(
    InputContextSnapshot Snapshot,
    TargetDescriptor Target,
    bool HasEditableFocus,
    bool? IsReadOnly,
    bool? HasSelection,
    ScreenRect? UiAutomationCaret,
    UiAutomationCaretMethod UiAutomationCaretMethod,
    TextPattern2Status TextPattern2Status,
    ScreenRect? Win32Caret,
    ScreenRect? MsaaCaret,
    ProbeIssue Issue,
    double DurationMilliseconds,
    InputStateEvidence? InputStateEvidence = null);
