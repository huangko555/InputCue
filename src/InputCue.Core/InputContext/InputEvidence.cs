namespace InputCue.Core.InputContext;

public sealed record InputEvidence(
    bool HasEditableFocus,
    bool? IsReadOnly,
    bool? HasSelection,
    ScreenRect? UiAutomationCaret,
    ScreenRect? Win32Caret,
    ScreenRect? MsaaCaret,
    ProbeIssue Issue = ProbeIssue.None);
