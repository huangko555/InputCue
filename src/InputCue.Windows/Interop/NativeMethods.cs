using System.Runtime.InteropServices;

namespace InputCue.Windows.Interop;

internal static partial class NativeMethods
{
    internal const uint CoInitMultiThreaded = 0x0;
    internal const uint ClassContextInprocServer = 0x1;
    internal const uint EventObjectFocus = 0x8005;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const uint EventSystemForeground = 0x0003;
    internal const int RpcChangedMode = unchecked((int)0x80010106);
    internal const uint ObjectIdCaret = 0xFFFFFFF8;
    internal const int ObjectIdWindow = 0;
    internal const uint MonitorDefaultToNearest = 0x00000002;
    internal const uint PeekMessageNoRemove = 0x0000;
    internal const uint SendMessageTimeoutBlock = 0x0001;
    internal const uint SendMessageTimeoutAbortIfHung = 0x0002;
    internal const uint SendMessageTimeoutErrorOnExit = 0x0020;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WmQuit = 0x0012;
    internal const uint WmImeControl = 0x0283;

    internal static readonly Guid IAccessibleId = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint window, out NativeRect rectangle);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    internal static partial nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint window, char[] className, int maximumCount);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        nint module,
        WinEventCallback callback,
        uint processId,
        uint threadId,
        uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(
        out NativeMessage message,
        nint window,
        uint messageFilterMinimum,
        uint messageFilterMaximum,
        uint removeMessage);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(
        out NativeMessage message,
        nint window,
        uint messageFilterMinimum,
        uint messageFilterMaximum);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in NativeMessage message);

    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(
        in Guid classId,
        nint outer,
        uint context,
        in Guid interfaceId,
        out nint instance);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    internal static partial nint GetKeyboardLayout(uint threadId);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ImmIsIME(nint keyboardLayout);

    [LibraryImport("imm32.dll")]
    internal static partial nint ImmGetContext(nint window);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ImmReleaseContext(nint window, nint inputContext);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ImmGetOpenStatus(nint inputContext);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ImmGetConversionStatus(
        nint inputContext,
        out uint conversionMode,
        out uint sentenceMode);

    [LibraryImport("imm32.dll")]
    internal static partial nint ImmGetDefaultIMEWnd(nint window);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static partial nint SendMessageTimeoutW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nuint result);

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

internal delegate void WinEventCallback(
    nint hook,
    uint eventType,
    nint window,
    int objectId,
    int childId,
    uint eventThreadId,
    uint eventTimeMilliseconds);

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    internal nint Window;
    internal uint Message;
    internal nuint WParam;
    internal nint LParam;
    internal uint Time;
    internal NativePoint Point;
    internal uint Private;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ProcessEntry32
{
    internal uint Size;
    internal uint UsageCount;
    internal uint ProcessId;
    internal nuint DefaultHeapId;
    internal uint ModuleId;
    internal uint ThreadCount;
    internal uint ParentProcessId;
    internal int PriorityClassBase;
    internal uint Flags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    internal string ExecutableFile;

    internal static ProcessEntry32 Create() => new()
    {
        Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
        ExecutableFile = string.Empty,
    };
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

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
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
