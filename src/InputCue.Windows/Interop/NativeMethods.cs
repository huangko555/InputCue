using System.Runtime.InteropServices;

namespace InputCue.Windows.Interop;

internal static partial class NativeMethods
{
    internal const uint ObjectIdCaret = 0xFFFFFFF8;

    internal static readonly Guid IAccessibleId = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("oleacc.dll")]
    internal static extern int AccessibleObjectFromWindow(
        nint window,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object accessibleObject);
}

[StructLayout(LayoutKind.Sequential)]
internal struct GuiThreadInfo
{
    internal uint Size;
    internal uint Flags;
    internal nint ActiveWindow;
    internal nint FocusWindow;
    internal nint CaptureWindow;
    internal nint MenuOwnerWindow;
    internal nint MoveSizeWindow;
    internal nint CaretWindow;
    internal NativeRect CaretRectangle;

    internal static GuiThreadInfo Create() => new()
    {
        Size = (uint)Marshal.SizeOf<GuiThreadInfo>(),
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}
