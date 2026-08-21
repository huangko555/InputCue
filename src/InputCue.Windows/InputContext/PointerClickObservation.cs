using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal sealed record PointerClickObservation(
    DateTimeOffset ObservedAt,
    nint ForegroundWindow,
    int ProcessId,
    string WindowClassName,
    ScreenRect Anchor);
