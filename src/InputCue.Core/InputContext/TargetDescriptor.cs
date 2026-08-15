namespace InputCue.Core.InputContext;

public sealed record TargetDescriptor(
    int ProcessId,
    string ProcessName,
    string ControlType,
    string ClassName,
    string FrameworkId);
