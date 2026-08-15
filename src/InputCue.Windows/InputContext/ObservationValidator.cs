namespace InputCue.Windows.InputContext;

internal readonly record struct ObservationIdentity(
    nint ForegroundWindow,
    uint ForegroundProcessId,
    nint FocusWindow,
    int FocusedProcessId,
    int AutomationIdentity);

internal static class ObservationValidator
{
    internal static bool IsCurrent(
        ObservationIdentity initial,
        ObservationIdentity current) =>
        initial == current;
}
