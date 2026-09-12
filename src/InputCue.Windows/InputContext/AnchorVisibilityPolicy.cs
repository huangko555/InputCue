using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal static class AnchorVisibilityPolicy
{
    private static readonly ScreenRect KnownEmptyBounds = new(0, 0, 0, 0);

    internal static ScreenRect? KeepVisible(
        ScreenRect? anchor,
        ScreenRect? visibleBounds)
    {
        if (anchor is not { IsUsable: true } value || visibleBounds is null)
        {
            return anchor;
        }

        if (!visibleBounds.Value.IsUsable)
        {
            return null;
        }

        var viewport = visibleBounds.Value;
        return Intersect(value, viewport) is null ? null : value;
    }

    internal static ScreenRect? Intersect(ScreenRect first, ScreenRect second)
    {
        if (!first.IsUsable || !second.IsUsable)
        {
            return null;
        }

        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        return right > left && bottom > top
            ? new ScreenRect(left, top, right - left, bottom - top)
            : null;
    }

    internal static ScreenRect? ResolveVisibleBounds(
        ScreenRect? windowBounds,
        ScreenRect? targetBounds,
        bool targetIsOffscreen)
    {
        if (targetIsOffscreen)
        {
            return KnownEmptyBounds;
        }

        if (windowBounds is { IsUsable: true } window &&
            targetBounds is { IsUsable: true } target)
        {
            return Intersect(window, target) ?? KnownEmptyBounds;
        }

        return targetBounds is { IsUsable: true }
            ? targetBounds
            : windowBounds;
    }

    internal static ScreenRect? ResolveVisibleBounds(
        ScreenRect? windowBounds,
        ScreenRect? targetBounds,
        bool targetIsOffscreen,
        IEnumerable<ScreenRect>? verticalClippingBounds)
    {
        var visibleBounds = ResolveVisibleBounds(
            windowBounds,
            targetBounds,
            targetIsOffscreen);
        if (visibleBounds is not { IsUsable: true } visible ||
            verticalClippingBounds is null)
        {
            return visibleBounds;
        }

        foreach (var clippingBounds in verticalClippingBounds)
        {
            if (!clippingBounds.IsUsable ||
                !OverlapsHorizontally(visible, clippingBounds))
            {
                continue;
            }

            var top = Math.Max(visible.Y, clippingBounds.Y);
            var bottom = Math.Min(
                visible.Y + visible.Height,
                clippingBounds.Y + clippingBounds.Height);
            if (bottom <= top)
            {
                return KnownEmptyBounds;
            }

            visible = new ScreenRect(visible.X, top, visible.Width, bottom - top);
        }

        return visible;
    }

    private static bool OverlapsHorizontally(ScreenRect first, ScreenRect second) =>
        Math.Min(first.X + first.Width, second.X + second.Width) >
        Math.Max(first.X, second.X);
}
