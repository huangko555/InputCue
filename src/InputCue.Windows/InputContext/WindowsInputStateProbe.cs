using InputCue.Core.InputContext;
using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

internal sealed class WindowsInputStateProbe
{
    private const ushort ChinesePrimaryLanguage = 0x0004;
    private const uint ImeConversionNative = 0x0001;

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
        var state = Classify(facts);

        return new InputStateObservation(
            state,
            targetWindow,
            facts.ThreadId,
            facts.KeyboardLayout,
            Evidence(facts));
    }

    private static InputState Classify(InputStateFacts facts)
    {
        if (facts.ThreadId == 0 || facts.KeyboardLayout == 0)
        {
            return InputState.Unknown;
        }

        if (!facts.IsIme)
        {
            return InputState.English;
        }

        var languageId = (ushort)(facts.KeyboardLayout.ToInt64() & 0xFFFF);
        if ((languageId & 0x03FF) != ChinesePrimaryLanguage)
        {
            return InputState.Unknown;
        }

        var directOpen = facts.ImeOpen;
        bool? windowOpen = facts.DefaultImeWindow.OpenStatus is { } status
            ? status != 0
            : null;
        if (directOpen is not null && windowOpen is not null && directOpen != windowOpen)
        {
            return InputState.Unknown;
        }

        var open = directOpen ?? windowOpen;
        if (open is null)
        {
            return InputState.Unknown;
        }

        if (!open.Value)
        {
            return InputState.English;
        }

        var directNative = facts.ConversionMode is { } directMode
            ? (directMode & ImeConversionNative) != 0
            : (bool?)null;
        var windowNative = facts.DefaultImeWindow.ConversionMode is { } windowMode
            ? (windowMode & ImeConversionNative) != 0
            : (bool?)null;
        if (directNative is not null && windowNative is not null && directNative != windowNative)
        {
            return InputState.Unknown;
        }

        var native = directNative ?? windowNative;
        return native is null
            ? InputState.Unknown
            : native.Value
                ? InputState.Chinese
                : InputState.English;
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
            current.KeyboardLayout == observation.KeyboardLayout &&
            Evidence(current) == observation.Evidence;
    }

    private static InputStateEvidence Evidence(InputStateFacts facts)
    {
        if (facts.ThreadId == 0 || facts.KeyboardLayout == 0)
        {
            return InputStateEvidence.Unavailable;
        }

        return new InputStateEvidence(
            (ushort)(facts.KeyboardLayout.ToInt64() & 0xFFFF),
            facts.IsIme,
            facts.IsIme ? facts.HasImeContext : null,
            facts.ImeOpen,
            facts.ConversionMode,
            facts.DefaultImeWindow.HasDefaultImeWindow,
            facts.DefaultImeWindow.OpenStatus,
            facts.DefaultImeWindow.ConversionMode);
    }
}

internal sealed record InputStateObservation(
    InputState State,
    nint TargetWindow,
    uint ThreadId,
    nint KeyboardLayout,
    InputStateEvidence Evidence)
{
    internal static readonly InputStateObservation Unknown = new(
        InputState.Unknown,
        0,
        0,
        0,
        InputStateEvidence.Unavailable);
}

internal readonly record struct InputStateFacts(
    uint ThreadId,
    nint KeyboardLayout,
    bool IsIme,
    bool? HasImeContext = null,
    bool? ImeOpen = null,
    uint? ConversionMode = null,
    DefaultImeWindowFacts DefaultImeWindow = default);

internal interface IInputStateFactsReader
{
    public InputStateFacts Read(nint targetWindow);
}

internal sealed class WindowsInputStateFactsReader : IInputStateFactsReader
{
    internal static readonly WindowsInputStateFactsReader Instance = new();
    private readonly DefaultImeWindowProbe _defaultImeWindowProbe;

    private WindowsInputStateFactsReader()
        : this(new DefaultImeWindowProbe())
    {
    }

    internal WindowsInputStateFactsReader(DefaultImeWindowProbe defaultImeWindowProbe)
    {
        ArgumentNullException.ThrowIfNull(defaultImeWindowProbe);
        _defaultImeWindowProbe = defaultImeWindowProbe;
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
        if (keyboardLayout == 0)
        {
            return default;
        }

        var isIme = NativeMethods.ImmIsIME(keyboardLayout);
        if (!isIme)
        {
            return new InputStateFacts(threadId, keyboardLayout, false);
        }

        var defaultImeWindow = _defaultImeWindowProbe.Observe(targetWindow);
        var inputContext = NativeMethods.ImmGetContext(targetWindow);
        if (inputContext == 0)
        {
            return new InputStateFacts(
                threadId,
                keyboardLayout,
                true,
                HasImeContext: false,
                DefaultImeWindow: defaultImeWindow);
        }

        try
        {
            var imeOpen = NativeMethods.ImmGetOpenStatus(inputContext);
            var hasConversionStatus = NativeMethods.ImmGetConversionStatus(
                inputContext,
                out var conversionMode,
                out _);
            return new InputStateFacts(
                threadId,
                keyboardLayout,
                true,
                HasImeContext: true,
                ImeOpen: imeOpen,
                ConversionMode: hasConversionStatus ? conversionMode : null,
                DefaultImeWindow: defaultImeWindow);
        }
        finally
        {
            _ = NativeMethods.ImmReleaseContext(targetWindow, inputContext);
        }
    }
}
