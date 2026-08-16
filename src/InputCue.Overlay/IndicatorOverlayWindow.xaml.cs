using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using InputCue.Core.Indicator;
using InputCue.Core.InputContext;

namespace InputCue.Overlay;

public partial class IndicatorOverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private const int ToolWindowStyle = 0x00000080;
    private const int TransparentStyle = 0x00000020;
    private const uint NoActivatePosition = 0x0010;
    private const uint NoSizePosition = 0x0001;
    private const uint ShowWindowPosition = 0x0040;
    private const int AnchorGapDip = 6;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private static readonly SolidColorBrush ChineseBrush = FrozenBrush("#E5534B");
    private static readonly SolidColorBrush EnglishBrush = FrozenBrush("#2F7FD6");
    private static readonly SolidColorBrush EnglishUsBrush = FrozenBrush("#D99000");
    private static readonly SolidColorBrush CapsLockBrush = FrozenBrush("#2F9E68");

    private nint _windowHandle;

    internal IndicatorOverlayWindow()
    {
        InitializeComponent();
    }

    internal void Render(IndicatorViewState state)
    {
        if (!state.IsVisible || state.Anchor is not { IsUsable: true } anchor)
        {
            Hide();
            return;
        }

        IndicatorDot.Fill = state.InputState switch
        {
            InputState.Chinese => ChineseBrush,
            InputState.English => EnglishBrush,
            InputState.EnglishUs => EnglishUsBrush,
            InputState.CapsLock => CapsLockBrush,
            _ => Brushes.Transparent,
        };
        Opacity = state.Opacity;

        var isFirstFrame = !IsVisible;
        if (isFirstFrame)
        {
            Show();
            Position(anchor);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtr(_windowHandle, ExtendedStyleIndex).ToInt64();
        _ = SetWindowLongPtr(
            _windowHandle,
            ExtendedStyleIndex,
            new nint(extendedStyle | NoActivateStyle | ToolWindowStyle | TransparentStyle));
    }

    private void Position(ScreenRect anchor)
    {
        if (_windowHandle == 0)
        {
            return;
        }

        var monitor = MonitorFromRect(NativeRect.From(anchor), MonitorDefaultToNearest);
        var monitorInfo = MonitorInfo.Create();
        if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        // UIA, MSAA and ClientToScreen anchors are physical screen pixels. Keep them
        // in that coordinate space and scale only the WPF-authored visual gap.
        var dpi = GetMonitorDpi(monitor);
        var overlaySize = new PixelSize(
            OverlayPlacement.ScaleDipToPixels(ActualWidth, dpi),
            OverlayPlacement.ScaleDipToPixels(ActualHeight, dpi));
        var position = OverlayPlacement.Calculate(
            anchor,
            monitorInfo.WorkArea.ToPixelRect(),
            overlaySize,
            OverlayPlacement.ScaleDipToPixels(AnchorGapDip, dpi));
        _ = SetWindowPos(
            _windowHandle,
            -1,
            position.X,
            position.Y,
            0,
            0,
            NoActivatePosition | NoSizePosition | ShowWindowPosition);
    }

    private static SolidColorBrush FrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private uint GetMonitorDpi(nint monitor)
    {
        const int effectiveDpi = 0;
        if (GetDpiForMonitor(monitor, effectiveDpi, out var dpiX, out _) >= 0 && dpiX > 0)
        {
            return dpiX;
        }

        var windowDpi = GetDpiForWindow(_windowHandle);
        return windowDpi > 0 ? windowDpi : 96;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint window, int index, nint newValue);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromRect(in NativeRect rectangle, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);

        internal static NativeRect From(ScreenRect rectangle) => new()
        {
            Left = checked((int)Math.Round(rectangle.X)),
            Top = checked((int)Math.Round(rectangle.Y)),
            Right = checked((int)Math.Round(rectangle.X + rectangle.Width)),
            Bottom = checked((int)Math.Round(rectangle.Y + rectangle.Height)),
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;

        internal static MonitorInfo Create() => new()
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
    }
}
