using System.Windows.Automation;
using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

internal static class WindowsObservationIdentity
{
    internal static bool TryRead(out ObservationIdentity identity)
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(
            foregroundWindow,
            out var foregroundProcessId);
        if (foregroundThread == 0)
        {
            identity = default;
            return false;
        }

        var threadInfo = GuiThreadInfo.Create();
        if (!NativeMethods.GetGUIThreadInfo(foregroundThread, ref threadInfo))
        {
            identity = default;
            return false;
        }

        var focusedElement = AutomationElement.FocusedElement;
        if (focusedElement is null)
        {
            identity = default;
            return false;
        }

        identity = new ObservationIdentity(
            foregroundWindow,
            foregroundProcessId,
            threadInfo.FocusWindow,
            focusedElement.Current.ProcessId,
            AutomationIdentity(focusedElement));
        return true;
    }

    internal static int AutomationIdentity(AutomationElement element)
    {
        var hash = new HashCode();
        foreach (var value in element.GetRuntimeId())
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
