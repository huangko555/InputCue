using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal static class FeishuDocumentCaretPolicy
{
    // Feishu Docs keeps returning a caret rectangle after the real caret has scrolled away,
    // but pins that rectangle to the top or bottom of the document viewport. Other Chromium
    // editors may legitimately place a caret at an edge, so keep this workaround profile-only.
    private const double EdgeTolerance = 8;

    internal static ScreenRect? KeepUnclamped(
        ScreenRect? caret,
        ScreenRect? visibleBounds,
        bool isFeishuDocument)
    {
        if (!isFeishuDocument ||
            caret is not { IsUsable: true } value ||
            visibleBounds is not { IsUsable: true } viewport)
        {
            return caret;
        }

        var caretBottom = value.Y + value.Height;
        var viewportBottom = viewport.Y + viewport.Height;
        return value.Y <= viewport.Y + EdgeTolerance ||
            caretBottom >= viewportBottom - EdgeTolerance
                ? null
                : value;
    }
}
