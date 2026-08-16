using System.Windows.Automation;

namespace InputCue.Windows.InputContext;

internal static class AppProfileCatalog
{
    internal static bool SupportsWritableWpsDocumentSurface(
        string? processName,
        string? className,
        string? frameworkId,
        ControlType? controlType,
        bool hasValuePattern) =>
        string.Equals(processName, "wps", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(className, "KxWpsView", StringComparison.Ordinal) &&
        string.Equals(frameworkId, "Qt", StringComparison.Ordinal) &&
        controlType == ControlType.Group &&
        hasValuePattern;

    internal static bool SupportsWritableWindowsTerminalSurface(
        string? processName,
        string? className,
        string? frameworkId,
        ControlType? controlType) =>
        string.Equals(processName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(className, "TermControl", StringComparison.Ordinal) &&
        string.Equals(frameworkId, "XAML", StringComparison.Ordinal) &&
        controlType == ControlType.Text;
}
