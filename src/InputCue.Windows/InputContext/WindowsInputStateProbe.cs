using InputCue.Core.InputContext;
using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

internal sealed class WindowsInputStateProbe
{
    private readonly IInputStateFactsReader _factsReader;

    internal WindowsInputStateProbe()
        : this(WindowsInputStateFactsReader.Instance)
    {
    }

    internal WindowsInputStateProbe(IInputStateFactsReader factsReader)
    {
        ArgumentNullException.ThrowIfNull(factsReader);
        _factsReader = factsReader;
    }

    internal InputStateObservation Observe(nint targetWindow)
    {
        var facts = _factsReader.Read(targetWindow);
        // HKL identifies an IME, not its current conversion mode. Until a concrete
        // IME profile is validated, treating it as Chinese would create false cues.
        var state = facts.ThreadId == 0 || facts.KeyboardLayout == 0 || facts.IsIme
            ? InputState.Unknown
            : InputState.English;

        return new InputStateObservation(
            state,
            targetWindow,
            facts.ThreadId,
            facts.KeyboardLayout);
    }

    internal bool IsCurrent(InputStateObservation observation)
    {
        if (observation.TargetWindow == 0 ||
            observation.ThreadId == 0 ||
            observation.KeyboardLayout == 0)
        {
            return observation.State is InputState.Unknown;
        }

        var current = _factsReader.Read(observation.TargetWindow);
        return current.ThreadId == observation.ThreadId &&
            current.KeyboardLayout == observation.KeyboardLayout;
    }
}

internal sealed record InputStateObservation(
    InputState State,
    nint TargetWindow,
    uint ThreadId,
    nint KeyboardLayout)
{
    internal static readonly InputStateObservation Unknown = new(
        InputState.Unknown,
        0,
        0,
        0);
}

internal readonly record struct InputStateFacts(
    uint ThreadId,
    nint KeyboardLayout,
    bool IsIme);

internal interface IInputStateFactsReader
{
    public InputStateFacts Read(nint targetWindow);
}

internal sealed class WindowsInputStateFactsReader : IInputStateFactsReader
{
    internal static readonly WindowsInputStateFactsReader Instance = new();

    private WindowsInputStateFactsReader()
    {
    }

    public InputStateFacts Read(nint targetWindow)
    {
        if (targetWindow == 0)
        {
            return default;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
        if (threadId == 0)
        {
            return default;
        }

        var keyboardLayout = NativeMethods.GetKeyboardLayout(threadId);
        return keyboardLayout == 0
            ? default
            : new InputStateFacts(
                threadId,
                keyboardLayout,
                NativeMethods.ImmIsIME(keyboardLayout));
    }
}
