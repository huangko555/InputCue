using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

/// <summary>
/// Converts foreground and native focus WinEvents into one process-local change signal.
/// The hook thread owns the message queue required by out-of-context WinEvent callbacks.
/// </summary>
internal sealed class NativeFocusEventHook : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly Action _signal;
    private readonly WinEventCallback _callback;
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private readonly Thread _thread;
    private uint _threadId;
    private bool _disposed;

    internal NativeFocusEventHook(Action signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _signal = signal;
        _callback = OnWinEvent;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "InputCue.WinEvent",
        };
        _thread.Start();
        _ = _ready.Wait(StartupTimeout);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_threadId != 0)
        {
            _ = NativeMethods.PostThreadMessage(_threadId, NativeMethods.WmQuit, 0, 0);
        }

        if (_thread.Join(ShutdownTimeout))
        {
            _ready.Dispose();
        }
    }

    private void Run()
    {
        nint foregroundHook = 0;
        nint focusHook = 0;
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            _ = NativeMethods.PeekMessage(
                out _,
                0,
                0,
                0,
                NativeMethods.PeekMessageNoRemove);
            foregroundHook = Subscribe(NativeMethods.EventSystemForeground);
            focusHook = Subscribe(NativeMethods.EventObjectFocus);
            _ready.Set();

            while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
            {
                _ = NativeMethods.TranslateMessage(in message);
                _ = NativeMethods.DispatchMessage(in message);
            }
        }
        finally
        {
            if (focusHook != 0)
            {
                _ = NativeMethods.UnhookWinEvent(focusHook);
            }

            if (foregroundHook != 0)
            {
                _ = NativeMethods.UnhookWinEvent(foregroundHook);
            }

            _ready.Set();
        }
    }

    private nint Subscribe(uint eventType) => NativeMethods.SetWinEventHook(
        eventType,
        eventType,
        0,
        _callback,
        0,
        0,
        NativeMethods.WinEventOutOfContext);

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTimeMilliseconds)
    {
        if (!_disposed)
        {
            _signal();
        }
    }
}
