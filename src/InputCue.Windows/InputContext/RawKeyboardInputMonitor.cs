using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using InputCue.Core.InputContext;
using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

/// <summary>
/// Reports anonymous editing-key presses and validated pointer clicks from Raw Input without
/// retaining key codes or text.
/// </summary>
public sealed partial class RawKeyboardInputMonitor : IDisposable
{
    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort KeyboardUsage = 0x06;
    private const ushort MouseUsage = 0x02;
    private const uint RawInputCommand = 0x10000003;
    private const uint RawInputDeviceKeyboard = 1;
    private const uint RawInputDeviceMouse = 0;
    private const uint RawInputInputSink = 0x00000100;
    private const uint RawInputRemove = 0x00000001;
    private const uint WmInput = 0x00FF;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSystemKeyDown = 0x0104;
    private const ushort MouseLeftButtonDown = 0x0001;
    private const ushort MouseLeftButtonUp = 0x0002;
    private const ushort MouseRightButtonDown = 0x0004;
    private const ushort MouseMiddleButtonDown = 0x0010;
    private const ushort MouseExtraButtonDown = 0x0040;
    private const ushort MouseWheel = 0x0400;
    private const ushort MouseHorizontalWheel = 0x0800;
    private const int SystemMetricDragWidth = 68;
    private const int SystemMetricDragHeight = 69;
    private const int VirtualKeyShift = 0x10;
    private const int WindowClassCapacity = 256;

    private HwndSource? _source;
    private NativePoint? _leftButtonDownAt;
    private bool _disposed;

    public event EventHandler? EditingKeyPressed;
    internal event Action<PointerClickObservation>? PointerClickObserved;
    internal event EventHandler? PointerAnchorInvalidated;

    public bool Attach(Window window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(window);
        if (_source is not null)
        {
            return true;
        }

        var windowHandle = new WindowInteropHelper(window).EnsureHandle();
        if (windowHandle == 0)
        {
            return false;
        }

        var source = HwndSource.FromHwnd(windowHandle);
        if (source is null ||
            !Register(windowHandle, RawInputInputSink, GenericDesktopUsagePage, KeyboardUsage))
        {
            return false;
        }

        if (!Register(windowHandle, RawInputInputSink, GenericDesktopUsagePage, MouseUsage))
        {
            _ = Register(0, RawInputRemove, GenericDesktopUsagePage, KeyboardUsage);
            return false;
        }

        _source = source;
        _source.AddHook(WndProc);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
            _ = Register(0, RawInputRemove, GenericDesktopUsagePage, KeyboardUsage);
            _ = Register(0, RawInputRemove, GenericDesktopUsagePage, MouseUsage);
        }

        EditingKeyPressed = null;
        PointerClickObserved = null;
        PointerAnchorInvalidated = null;
    }

    private nint WndProc(
        nint window,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != WmInput || !TryReadRawInput(lParam, out var rawInput))
        {
            return 0;
        }

        if (IsEditingKey(rawInput))
        {
            EditingKeyPressed?.Invoke(this, EventArgs.Empty);
        }

        if (rawInput.Header.Type == RawInputDeviceMouse)
        {
            ObservePointerButtons(rawInput.Data.Mouse.ButtonFlags);
        }

        return 0;
    }

    private static bool TryReadRawInput(nint rawInputHandle, out RawInput rawInput)
    {
        var size = (uint)Marshal.SizeOf<RawInput>();
        var result = GetRawInputData(
            rawInputHandle,
            RawInputCommand,
            out rawInput,
            ref size,
            (uint)Marshal.SizeOf<RawInputHeader>());
        return result != uint.MaxValue;
    }

    private static bool IsEditingKey(RawInput rawInput) =>
        rawInput.Header.Type == RawInputDeviceKeyboard &&
            rawInput.Keyboard.Message is WmKeyDown or WmSystemKeyDown &&
            RawKeyboardInputClassifier.IsEditingKey(rawInput.Keyboard.VirtualKey);

    private void ObservePointerButtons(ushort buttonFlags)
    {
        const ushort invalidatingActivity = MouseLeftButtonDown |
            MouseRightButtonDown |
            MouseMiddleButtonDown |
            MouseExtraButtonDown |
            MouseWheel |
            MouseHorizontalWheel;
        if ((buttonFlags & invalidatingActivity) != 0)
        {
            PointerAnchorInvalidated?.Invoke(this, EventArgs.Empty);
        }

        if ((buttonFlags & MouseLeftButtonDown) != 0)
        {
            _leftButtonDownAt = NativeMethods.GetCursorPos(out var point) ? point : null;
        }

        if ((buttonFlags & MouseLeftButtonUp) == 0)
        {
            return;
        }

        var pointerDownAt = _leftButtonDownAt;
        _leftButtonDownAt = null;
        if (pointerDownAt is not { } down ||
            !NativeMethods.GetCursorPos(out var up) ||
            IsShiftPressed() ||
            IsDrag(down, up))
        {
            return;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var hitWindow = NativeMethods.WindowFromPoint(up);
        if (foregroundWindow == 0 || hitWindow == 0)
        {
            return;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hitWindow, out var processId);
        var className = new char[WindowClassCapacity];
        var classNameLength = NativeMethods.GetClassName(hitWindow, className, className.Length);
        if (processId == 0 || classNameLength <= 0)
        {
            return;
        }

        PointerClickObserved?.Invoke(new PointerClickObservation(
            DateTimeOffset.UtcNow,
            foregroundWindow,
            checked((int)processId),
            new string(className, 0, classNameLength),
            new ScreenRect(up.X, up.Y, 1, 1)));
    }

    private static bool IsShiftPressed() =>
        (NativeMethods.GetAsyncKeyState(VirtualKeyShift) & 0x8000) != 0;

    private static bool IsDrag(NativePoint down, NativePoint up)
    {
        var maximumHorizontalMovement = Math.Max(1, NativeMethods.GetSystemMetrics(SystemMetricDragWidth));
        var maximumVerticalMovement = Math.Max(1, NativeMethods.GetSystemMetrics(SystemMetricDragHeight));
        return Math.Abs(up.X - down.X) >= maximumHorizontalMovement ||
            Math.Abs(up.Y - down.Y) >= maximumVerticalMovement;
    }

    private static bool Register(nint targetWindow, uint flags, ushort usagePage, ushort usage)
    {
        var device = new RawInputDevice
        {
            UsagePage = usagePage,
            Usage = usage,
            Flags = flags,
            TargetWindow = targetWindow,
        };
        return RegisterRawInputDevices(
            in device,
            1,
            (uint)Marshal.SizeOf<RawInputDevice>());
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices(
        in RawInputDevice devices,
        uint deviceCount,
        uint deviceSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetRawInputData(
        nint rawInput,
        uint command,
        out RawInput data,
        ref uint size,
        uint headerSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint TargetWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        internal uint Type;
        internal uint Size;
        internal nint Device;
        internal nuint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        internal ushort MakeCode;
        internal ushort Flags;
        internal ushort Reserved;
        internal ushort VirtualKey;
        internal uint Message;
        internal uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        internal ushort Flags;
        internal ushort Padding;
        internal ushort ButtonFlags;
        internal ushort ButtonData;
        internal uint RawButtons;
        internal int LastX;
        internal int LastY;
        internal uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawInputData
    {
        [FieldOffset(0)]
        internal RawMouse Mouse;

        [FieldOffset(0)]
        internal RawKeyboard Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        internal RawInputHeader Header;
        internal RawInputData Data;

        internal readonly RawKeyboard Keyboard => Data.Keyboard;
    }
}

internal static class RawKeyboardInputClassifier
{
    internal static bool IsEditingKey(ushort virtualKey) =>
        virtualKey is >= 0x30 and <= 0x39 ||
        virtualKey is >= 0x41 and <= 0x5A ||
        virtualKey is >= 0x60 and <= 0x6F ||
        virtualKey is >= 0xBA and <= 0xE2 ||
        virtualKey is 0x08 or 0x09 or 0x0D or 0x20 or 0x2E or 0xE7;
}
