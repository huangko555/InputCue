using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal static class CaretAnchorPolicy
{
    internal static ScreenRect? KeepCaretLike(ScreenRect? candidate)
    {
        if (candidate is not { IsUsable: true } caret)
        {
            return null;
        }

        return caret.Width <= Math.Max(8, caret.Height / 2) &&
            caret.Height <= 256
                ? caret
                : null;
    }
}
