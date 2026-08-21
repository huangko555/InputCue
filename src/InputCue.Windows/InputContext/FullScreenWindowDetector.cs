using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

public static class FullScreenWindowDetector
{
    private const int BoundsTolerancePixels = 2;

    public static bool IsForegroundWindowFullScreen()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0 ||
            window == NativeMethods.GetShellWindow() ||
            !NativeMethods.IsWindowVisible(window) ||
            NativeMethods.IsIconic(window) ||
            !NativeMethods.GetWindowRect(window, out var windowBounds))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(
            window,
            NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = MonitorInfo.Create();
        return monitor != 0 &&
            NativeMethods.GetMonitorInfo(monitor, ref monitorInfo) &&
            IsFullScreenBounds(windowBounds, monitorInfo.Monitor);
    }

    internal static bool IsFullScreenBounds(NativeRect window, NativeRect monitor) =>
        HasPositiveArea(window) &&
        HasPositiveArea(monitor) &&
        IsNear(window.Left, monitor.Left) &&
        IsNear(window.Top, monitor.Top) &&
        IsNear(window.Right, monitor.Right) &&
        IsNear(window.Bottom, monitor.Bottom);

    private static bool HasPositiveArea(NativeRect bounds) =>
        bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;

    private static bool IsNear(int first, int second) =>
        Math.Abs((long)first - second) <= BoundsTolerancePixels;
}
