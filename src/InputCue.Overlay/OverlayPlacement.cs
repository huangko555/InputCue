using InputCue.Core.InputContext;

namespace InputCue.Overlay;

internal static class OverlayPlacement
{
    internal static PixelPoint Calculate(
        ScreenRect anchor,
        PixelRect workArea,
        PixelSize overlaySize,
        int gap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        var anchorLeft = RoundToInt(anchor.X);
        var anchorRight = RoundToInt(anchor.X + anchor.Width);
        var anchorBottom = RoundToInt(anchor.Y + anchor.Height);

        var x = anchorRight + gap;
        if (x + overlaySize.Width > workArea.Right)
        {
            x = anchorLeft - gap - overlaySize.Width;
        }

        var y = anchorBottom - overlaySize.Height;
        return new PixelPoint(
            Clamp(x, workArea.Left, workArea.Right - overlaySize.Width),
            Clamp(y, workArea.Top, workArea.Bottom - overlaySize.Height));
    }

    internal static int ScaleDipToPixels(double value, uint dpi) =>
        checked((int)Math.Round(value * dpi / 96d, MidpointRounding.AwayFromZero));

    private static int RoundToInt(double value) =>
        checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private static int Clamp(int value, int minimum, int maximum) =>
        maximum < minimum ? minimum : Math.Clamp(value, minimum, maximum);
}

internal readonly record struct PixelPoint(int X, int Y);

internal readonly record struct PixelSize(int Width, int Height);

internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);
