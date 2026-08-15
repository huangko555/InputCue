namespace InputCue.Core.InputContext;

public sealed record InputContextDiagnostic(
    InputContextSnapshot Snapshot,
    TargetDescriptor Target,
    bool? IsReadOnly,
    bool? HasSelection,
    ScreenRect? UiAutomationCaret,
    ScreenRect? Win32Caret,
    ScreenRect? MsaaCaret,
    ProbeIssue Issue,
    double DurationMilliseconds);
