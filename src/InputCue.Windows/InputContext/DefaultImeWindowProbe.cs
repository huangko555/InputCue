using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

internal sealed class DefaultImeWindowProbe
{
    internal const uint GetConversionMode = 0x0001;
    internal const uint GetOpenStatus = 0x0005;

    private static readonly TimeSpan MessageTimeout = TimeSpan.FromMilliseconds(25);
    private readonly IDefaultImeWindowNative _native;

    internal DefaultImeWindowProbe()
        : this(WindowsDefaultImeWindowNative.Instance)
    {
    }

    internal DefaultImeWindowProbe(IDefaultImeWindowNative native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    internal DefaultImeWindowFacts Observe(nint targetWindow)
    {
        if (targetWindow == 0)
        {
            return default;
        }

        var imeWindow = _native.GetDefaultImeWindow(targetWindow);
        if (imeWindow == 0)
        {
            return new DefaultImeWindowFacts(false, null, null);
        }

        var openStatus = _native.Query(imeWindow, GetOpenStatus, MessageTimeout);
        var conversionMode = _native.Query(imeWindow, GetConversionMode, MessageTimeout);
        return new DefaultImeWindowFacts(
            true,
            ValueOrNull(openStatus),
            ValueOrNull(conversionMode));
    }

    private static uint? ValueOrNull(ImeWindowMessageResult result) =>
        result.Succeeded && result.Value <= uint.MaxValue
            ? (uint)result.Value
            : null;
}

internal readonly record struct DefaultImeWindowFacts(
    bool? HasDefaultImeWindow,
    uint? OpenStatus,
    uint? ConversionMode);

internal readonly record struct ImeWindowMessageResult(
    bool Succeeded,
    nuint Value);

internal interface IDefaultImeWindowNative
{
    public nint GetDefaultImeWindow(nint targetWindow);

    public ImeWindowMessageResult Query(
        nint imeWindow,
        uint command,
        TimeSpan timeout);
}

internal sealed class WindowsDefaultImeWindowNative : IDefaultImeWindowNative
{
    private const uint SendMessageFlags =
        NativeMethods.SendMessageTimeoutAbortIfHung |
        NativeMethods.SendMessageTimeoutBlock |
        NativeMethods.SendMessageTimeoutErrorOnExit;

    internal static readonly WindowsDefaultImeWindowNative Instance = new();

    private WindowsDefaultImeWindowNative()
    {
    }

    public nint GetDefaultImeWindow(nint targetWindow) =>
        NativeMethods.ImmGetDefaultIMEWnd(targetWindow);

    public ImeWindowMessageResult Query(
        nint imeWindow,
        uint command,
        TimeSpan timeout)
    {
        var timeoutMilliseconds = checked((uint)Math.Ceiling(timeout.TotalMilliseconds));
        var sendResult = NativeMethods.SendMessageTimeoutW(
            imeWindow,
            NativeMethods.WmImeControl,
            command,
            0,
            SendMessageFlags,
            timeoutMilliseconds,
            out var value);
        return new ImeWindowMessageResult(sendResult != 0, value);
    }
}
