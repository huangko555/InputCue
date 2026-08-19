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

    internal static bool SupportsWritableProseMirrorSurface(
        string? className,
        string? frameworkId,
        ControlType? controlType,
        bool hasTextPattern) =>
        string.Equals(frameworkId, "Chrome", StringComparison.Ordinal) &&
        controlType == ControlType.Group &&
        hasTextPattern &&
        ContainsClassToken(className, "ProseMirror") &&
        ContainsClassToken(className, "ProseMirror-focused");

    internal static bool SupportsWritableFeishuDocumentSurface(
        string? className,
        string? frameworkId,
        ControlType? controlType,
        bool hasTextPattern) =>
        string.Equals(frameworkId, "Chrome", StringComparison.Ordinal) &&
        controlType == ControlType.Group &&
        hasTextPattern &&
        ContainsClassToken(className, "page-block") &&
        ContainsClassToken(className, "root-block");

    internal static bool IsHiddenFeishuSelectionHelper(
        string? className,
        string? frameworkId,
        ControlType? controlType) =>
        string.Equals(frameworkId, "Chrome", StringComparison.Ordinal) &&
        controlType == ControlType.Edit &&
        ContainsClassToken(className, "docx-selection-hidden-textarea");

    internal static bool SupportsWritableFeishuSheetSurface(
        string? className,
        string? frameworkId,
        ControlType? controlType,
        bool hasTextPattern,
        bool hasRecordedAncestorShape) =>
        string.Equals(frameworkId, "Chrome", StringComparison.Ordinal) &&
        controlType == ControlType.Group &&
        string.IsNullOrWhiteSpace(className) &&
        hasTextPattern &&
        hasRecordedAncestorShape;

    internal static bool SupportsWritableFeishuBitableSurface(
        string? className,
        string? frameworkId,
        ControlType? controlType,
        bool hasTextPattern,
        bool hasActiveEditorAncestor) =>
        string.Equals(frameworkId, "Chrome", StringComparison.Ordinal) &&
        controlType == ControlType.Group &&
        hasTextPattern &&
        ContainsClassTokenStartingWith(className, "BITABLE_EDITOR_CONTAINER_") &&
        hasActiveEditorAncestor;

    internal static bool SupportsWritableFeishuChatSurface(
        string? processName,
        string? className,
        string? frameworkId,
        ControlType? controlType,
        bool hasTextPattern,
        bool hasRecordedParentShape) =>
        string.Equals(processName, "Feishu", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(frameworkId, "Chrome", StringComparison.Ordinal) &&
        controlType == ControlType.Group &&
        hasTextPattern &&
        ContainsClassToken(className, "zone-container") &&
        ContainsClassToken(className, "editor-kit-container") &&
        ContainsClassToken(className, "innerdocbody") &&
        hasRecordedParentShape;

    private static bool ContainsClassToken(string? className, string classPart) =>
        className?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(classPart, StringComparer.Ordinal) is true;

    private static bool ContainsClassTokenStartingWith(string? className, string prefix) =>
        className?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(classPart => classPart.StartsWith(prefix, StringComparison.Ordinal)) is true;
}
