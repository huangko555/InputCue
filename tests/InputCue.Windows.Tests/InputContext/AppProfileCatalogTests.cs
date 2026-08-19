using System.Windows.Automation;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class AppProfileCatalogTests
{
    [Fact]
    public void WpsDocumentSurfaceRequiresTheRecordedWritableShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableWpsDocumentSurface(
            "wps",
            "KxWpsView",
            "Qt",
            ControlType.Group,
            hasValuePattern: true));
    }

    [Theory]
    [InlineData("other", "KxWpsView", "Qt", true)]
    [InlineData("wps", "OtherView", "Qt", true)]
    [InlineData("wps", "KxWpsView", "Win32", true)]
    [InlineData("wps", "KxWpsView", "Qt", false)]
    public void WpsDocumentSurfaceRejectsNearMisses(
        string processName,
        string className,
        string frameworkId,
        bool hasValuePattern)
    {
        Assert.False(AppProfileCatalog.SupportsWritableWpsDocumentSurface(
            processName,
            className,
            frameworkId,
            ControlType.Group,
            hasValuePattern));
    }

    [Fact]
    public void WindowsTerminalSurfaceRequiresTheRecordedTextShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableWindowsTerminalSurface(
            "WindowsTerminal",
            "TermControl",
            "XAML",
            ControlType.Text));
    }

    [Theory]
    [InlineData("other", "TermControl", "XAML")]
    [InlineData("WindowsTerminal", "OtherControl", "XAML")]
    [InlineData("WindowsTerminal", "TermControl", "Win32")]
    public void WindowsTerminalSurfaceRejectsNearMisses(
        string processName,
        string className,
        string frameworkId)
    {
        Assert.False(AppProfileCatalog.SupportsWritableWindowsTerminalSurface(
            processName,
            className,
            frameworkId,
            ControlType.Text));
    }

    [Fact]
    public void WindowsTerminalSurfaceRejectsNonTextControl()
    {
        Assert.False(AppProfileCatalog.SupportsWritableWindowsTerminalSurface(
            "WindowsTerminal",
            "TermControl",
            "XAML",
            ControlType.Group));
    }

    [Fact]
    public void ProseMirrorSurfaceRequiresTheRecordedFocusedTextShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableProseMirrorSurface(
            "ProseMirror ProseMirror-focused",
            "Chrome",
            ControlType.Group,
            hasTextPattern: true));
    }

    [Theory]
    [InlineData("ProseMirror", "Chrome", true)]
    [InlineData("ProseMirror-focused", "Chrome", true)]
    [InlineData("ProseMirror ProseMirror-focused", "Other", true)]
    [InlineData("ProseMirror ProseMirror-focused", "Chrome", false)]
    public void ProseMirrorSurfaceRejectsNearMisses(
        string className,
        string frameworkId,
        bool hasTextPattern)
    {
        Assert.False(AppProfileCatalog.SupportsWritableProseMirrorSurface(
            className,
            frameworkId,
            ControlType.Group,
            hasTextPattern));
    }

    [Fact]
    public void ProseMirrorSurfaceRejectsNonGroupControl()
    {
        Assert.False(AppProfileCatalog.SupportsWritableProseMirrorSurface(
            "ProseMirror ProseMirror-focused",
            "Chrome",
            ControlType.Document,
            hasTextPattern: true));
    }

    [Fact]
    public void FeishuDocumentSurfaceRequiresTheRecordedWritableShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableFeishuDocumentSurface(
            "page-block root-block",
            "Chrome",
            ControlType.Group,
            hasTextPattern: true));
    }

    [Theory]
    [InlineData("page-block", "Chrome", true)]
    [InlineData("root-block", "Chrome", true)]
    [InlineData("page-block root-block", "Other", true)]
    [InlineData("page-block root-block", "Chrome", false)]
    public void FeishuDocumentSurfaceRejectsNearMisses(
        string className,
        string frameworkId,
        bool hasTextPattern)
    {
        Assert.False(AppProfileCatalog.SupportsWritableFeishuDocumentSurface(
            className,
            frameworkId,
            ControlType.Group,
            hasTextPattern));
    }

    [Fact]
    public void FeishuDocumentSurfaceRejectsNonGroupControl()
    {
        Assert.False(AppProfileCatalog.SupportsWritableFeishuDocumentSurface(
            "page-block root-block",
            "Chrome",
            ControlType.Document,
            hasTextPattern: true));
    }

    [Fact]
    public void HiddenFeishuSelectionHelperRequiresTheRecordedShape()
    {
        Assert.True(AppProfileCatalog.IsHiddenFeishuSelectionHelper(
            "docx-selection-hidden-textarea",
            "Chrome",
            ControlType.Edit));
    }

    [Theory]
    [InlineData("other", "Chrome")]
    [InlineData("docx-selection-hidden-textarea", "Other")]
    public void HiddenFeishuSelectionHelperRejectsNearMisses(
        string className,
        string frameworkId)
    {
        Assert.False(AppProfileCatalog.IsHiddenFeishuSelectionHelper(
            className,
            frameworkId,
            ControlType.Edit));
    }

    [Fact]
    public void HiddenFeishuSelectionHelperRejectsNonEditControl()
    {
        Assert.False(AppProfileCatalog.IsHiddenFeishuSelectionHelper(
            "docx-selection-hidden-textarea",
            "Chrome",
            ControlType.Group));
    }

    [Fact]
    public void FeishuSheetSurfaceRequiresTheRecordedAncestorShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableFeishuSheetSurface(
            string.Empty,
            "Chrome",
            ControlType.Group,
            hasTextPattern: true,
            hasRecordedAncestorShape: true));
    }

    [Theory]
    [InlineData("other", "Chrome", true, true)]
    [InlineData("", "Other", true, true)]
    [InlineData("", "Chrome", false, true)]
    [InlineData("", "Chrome", true, false)]
    public void FeishuSheetSurfaceRejectsNearMisses(
        string className,
        string frameworkId,
        bool hasTextPattern,
        bool hasRecordedAncestorShape)
    {
        Assert.False(AppProfileCatalog.SupportsWritableFeishuSheetSurface(
            className,
            frameworkId,
            ControlType.Group,
            hasTextPattern,
            hasRecordedAncestorShape));
    }

    [Fact]
    public void FeishuSheetSurfaceRejectsNonGroupControl()
    {
        Assert.False(AppProfileCatalog.SupportsWritableFeishuSheetSurface(
            string.Empty,
            "Chrome",
            ControlType.Edit,
            hasTextPattern: true,
            hasRecordedAncestorShape: true));
    }

    [Fact]
    public void FeishuBitableSurfaceRequiresTheRecordedActiveEditorShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableFeishuBitableSurface(
            "bitable-text-editor-container bitable-text-editor-container--active BITABLE_EDITOR_CONTAINER_tbl",
            "Chrome",
            ControlType.Group,
            hasTextPattern: true,
            hasActiveEditorAncestor: true));
    }

    [Theory]
    [InlineData("BITABLE_EDITOR_CONTAINER_tbl", "Chrome", true, false)]
    [InlineData("BITABLE_EDITOR_CONTAINER_tbl", "Other", true, true)]
    [InlineData("BITABLE_EDITOR_CONTAINER_tbl", "Chrome", false, true)]
    public void FeishuBitableSurfaceRejectsNearMisses(
        string className,
        string frameworkId,
        bool hasTextPattern,
        bool hasActiveEditorAncestor)
    {
        Assert.False(AppProfileCatalog.SupportsWritableFeishuBitableSurface(
            className,
            frameworkId,
            ControlType.Group,
            hasTextPattern,
            hasActiveEditorAncestor));
    }

    [Fact]
    public void FeishuChatSurfaceRequiresTheRecordedEditorShape()
    {
        Assert.True(AppProfileCatalog.SupportsWritableFeishuChatSurface(
            "Feishu",
            "zone-container editor-kit-container innerdocbody notranslate chrome window chrome88",
            "Chrome",
            ControlType.Group,
            hasTextPattern: true,
            hasRecordedParentShape: true));
    }

    [Theory]
    [InlineData("other", "zone-container editor-kit-container innerdocbody", "Chrome", true, true)]
    [InlineData("Feishu", "editor-kit-container innerdocbody", "Chrome", true, true)]
    [InlineData("Feishu", "zone-container editor-kit-container innerdocbody", "Other", true, true)]
    [InlineData("Feishu", "zone-container editor-kit-container innerdocbody", "Chrome", false, true)]
    [InlineData("Feishu", "zone-container editor-kit-container innerdocbody", "Chrome", true, false)]
    public void FeishuChatSurfaceRejectsNearMisses(
        string processName,
        string className,
        string frameworkId,
        bool hasTextPattern,
        bool hasRecordedParentShape)
    {
        Assert.False(AppProfileCatalog.SupportsWritableFeishuChatSurface(
            processName,
            className,
            frameworkId,
            ControlType.Group,
            hasTextPattern,
            hasRecordedParentShape));
    }

    [Fact]
    public void FeishuChatSurfaceRejectsNonGroupControl()
    {
        Assert.False(AppProfileCatalog.SupportsWritableFeishuChatSurface(
            "Feishu",
            "zone-container editor-kit-container innerdocbody",
            "Chrome",
            ControlType.Document,
            hasTextPattern: true,
            hasRecordedParentShape: true));
    }

}
