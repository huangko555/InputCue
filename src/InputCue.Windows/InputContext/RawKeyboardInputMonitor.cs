using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace InputCue.Windows.InputContext;

/// <summary>
/// Reports anonymous editing-key presses from Raw Input without retaining key codes or text.
/// </summary>
public sealed partial class RawKeyboardInputMonitor : IDisposable
{
    private const uint GenericDesktopUsagePage = 0x01;
    private const uint KeyboardUsage = 0x06;
    private const uint RawInputCommand = 0x10000003;
    private const uint RawInputDeviceKeyboard = 1;
    private const uint RawInputInputSink = 0x00000100;
    private const uint RawInputRemove = 0x00000001;
    private const uint WmInput = 0x00FF;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSystemKeyDown = 0x0104;

    private HwndSource? _source;
    private bool _disposed;

    public event EventHandler? EditingKeyPressed;

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
        if (source is null || !Register(windowHandle, RawInputInputSink))
        {
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
            _ = Register(0, RawInputRemove);
        }

        EditingKeyPressed = null;
    }

    private nint WndProc(
        nint window,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmInput && TryReadEditingKey(lParam))
        {
            EditingKeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    private static bool TryReadEditingKey(nint rawInputHandle)
    {
        var size = (uint)Marshal.SizeOf<RawInput>();
        var result = GetRawInputData(
            rawInputHandle,
            RawInputCommand,
            out var rawInput,
            ref size,
            (uint)Marshal.SizeOf<RawInputHeader>());
        return result != uint.MaxValue &&
            rawInput.Header.Type == RawInputDeviceKeyboard &&
            rawInput.Keyboard.Message is WmKeyDown or WmSystemKeyDown &&
            RawKeyboardInputClassifier.IsEditingKey(rawInput.Keyboard.VirtualKey);
    }

    private static bool Register(nint targetWindow, uint flags)
    {
        var device = new RawInputDevice
        {
            UsagePage = (ushort)GenericDesktopUsagePage,
            Usage = (ushort)KeyboardUsage,
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
    private struct RawInput
    {
        internal RawInputHeader Header;
        internal RawKeyboard Keyboard;
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
