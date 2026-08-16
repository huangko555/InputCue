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
    private const int AnchorGap = 6;

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

        var x = checked((int)Math.Round(anchor.X + anchor.Width + AnchorGap));
        var y = checked((int)Math.Round(anchor.Y + anchor.Height - ActualHeight));
        _ = SetWindowPos(
            _windowHandle,
            -1,
            x,
            y,
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
}
