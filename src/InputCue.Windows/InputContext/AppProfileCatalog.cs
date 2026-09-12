using System.Windows.Automation;
using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal static class AppProfileCatalog
{
    internal static bool SupportsPointerAnchorFallback(
        TargetDescriptor target,
        string hitWindowClassName) =>
        string.Equals(target.ProcessName, "wps", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(target.ClassName, "KxWpsView", StringComparison.Ordinal) &&
        string.Equals(target.FrameworkId, "Qt", StringComparison.Ordinal) &&
        string.Equals(target.ControlType, ControlType.Group.ProgrammaticName, StringComparison.Ordinal) &&
        (string.Equals(hitWindowClassName, target.ClassName, StringComparison.Ordinal) ||
            string.Equals(hitWindowClassName, "_WwG", StringComparison.Ordinal));

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

    internal static bool SupportsWritableBilibiliRichTextSurface(
        string? className,
        string? frameworkId,
        ControlType? controlType,
        bool hasTextPattern) =>
        string.Equals(frameworkId, "Chrome", StringComparison.Ordinal) &&
        controlType == ControlType.Group &&
        hasTextPattern &&
        ContainsClassToken(className, "brt-editor");

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

    internal static bool SupportsWritableFeishuDocumentSurface(TargetDescriptor target) =>
        string.Equals(target.FrameworkId, "Chrome", StringComparison.Ordinal) &&
        string.Equals(
            target.ControlType,
            ControlType.Group.ProgrammaticName,
            StringComparison.Ordinal) &&
        ContainsClassToken(target.ClassName, "page-block") &&
        ContainsClassToken(target.ClassName, "root-block");

    internal static bool IsHiddenFeishuSelectionHelper(
        string? className,
        string? frameworkId,
        ControlType? controlType) =>
        string.Equals(frameworkId, "Chrome", StringComparison.Ordinal) &&
        controlType == ControlType.Edit &&
        ContainsClassToken(className, "docx-selection-hidden-textarea");

    internal static bool IsHiddenFeishuSelectionHelper(TargetDescriptor target) =>
        string.Equals(target.FrameworkId, "Chrome", StringComparison.Ordinal) &&
        string.Equals(
            target.ControlType,
            ControlType.Edit.ProgrammaticName,
            StringComparison.Ordinal) &&
        ContainsClassToken(target.ClassName, "docx-selection-hidden-textarea");

    internal static bool SupportsFeishuDocumentFocusProxy(
        string? focusedClassName,
        string? focusedFrameworkId,
        ControlType? focusedControlType,
        string? documentClassName,
        string? documentFrameworkId,
        ControlType? documentControlType,
        bool documentHasTextPattern) =>
        IsHiddenFeishuSelectionHelper(
            focusedClassName,
            focusedFrameworkId,
            focusedControlType) &&
        SupportsWritableFeishuDocumentSurface(
            documentClassName,
            documentFrameworkId,
            documentControlType,
            documentHasTextPattern);

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
