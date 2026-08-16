using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace InputCue.Windows.InputContext;

internal sealed class WindowsInputContextEventSource : IInputContextEventSource
{
    private readonly AutomationFocusChangedEventHandler _focusChangedHandler;
    private readonly AutomationEventHandler _selectionChangedHandler;
    private readonly NativeFocusEventHook _nativeFocusHook;
    private readonly AutoResetEvent _signal = new(initialState: false);
    private bool _focusSubscribed;
    private bool _selectionSubscribed;
    private bool _disposed;

    private WindowsInputContextEventSource()
    {
        _focusChangedHandler = OnFocusChanged;
        _selectionChangedHandler = OnSelectionChanged;
        _nativeFocusHook = new NativeFocusEventHook(Signal);
    }

    internal static WindowsInputContextEventSource Create()
    {
        var source = new WindowsInputContextEventSource();
        source.Subscribe();
        return source;
    }

    public bool WaitForChange(TimeSpan fallbackInterval, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellation.ThrowIfCancellationRequested();

        var result = WaitHandle.WaitAny([_signal, cancellation.WaitHandle], fallbackInterval);
        cancellation.ThrowIfCancellationRequested();
        return result == 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _nativeFocusHook.Dispose();

        if (_selectionSubscribed)
        {
            TryRemoveSelectionHandler();
        }

        if (_focusSubscribed)
        {
            TryRemoveFocusHandler();
        }

        _signal.Dispose();
    }

    private void Subscribe()
    {
        try
        {
            Automation.AddAutomationFocusChangedEventHandler(_focusChangedHandler);
            _focusSubscribed = true;
        }
        catch (Exception exception) when (IsExpectedAutomationFailure(exception))
        {
        }

        try
        {
            Automation.AddAutomationEventHandler(
                TextPattern.TextSelectionChangedEvent,
                AutomationElement.RootElement,
                TreeScope.Subtree,
                _selectionChangedHandler);
            _selectionSubscribed = true;
        }
        catch (Exception exception) when (IsExpectedAutomationFailure(exception))
        {
        }

    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e) => Signal();

    private void OnSelectionChanged(object sender, AutomationEventArgs e) => Signal();

    private void Signal()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _signal.Set();
        }
        catch (ObjectDisposedException)
        {
            // An already queued UIA callback may race with shutdown.
        }
    }

    private void TryRemoveFocusHandler()
    {
        try
        {
            Automation.RemoveAutomationFocusChangedEventHandler(_focusChangedHandler);
        }
        catch (Exception exception) when (IsExpectedAutomationFailure(exception))
        {
        }
    }

    private void TryRemoveSelectionHandler()
    {
        try
        {
            Automation.RemoveAutomationEventHandler(
                TextPattern.TextSelectionChangedEvent,
                AutomationElement.RootElement,
                _selectionChangedHandler);
        }
        catch (Exception exception) when (IsExpectedAutomationFailure(exception))
        {
        }
    }

    private static bool IsExpectedAutomationFailure(Exception exception) =>
        exception is COMException or
            ElementNotAvailableException or
            InvalidOperationException or
            NotSupportedException or
            UnauthorizedAccessException;

}
