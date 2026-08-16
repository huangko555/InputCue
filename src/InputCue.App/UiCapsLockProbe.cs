using System.Runtime.InteropServices;

namespace InputCue.App;

internal static partial class UiCapsLockProbe
{
    private const int VirtualKeyCapital = 0x14;

    internal static bool IsEnabled() => (GetKeyState(VirtualKeyCapital) & 0x0001) != 0;

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int virtualKey);
}
