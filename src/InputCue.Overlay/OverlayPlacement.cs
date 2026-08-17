using InputCue.Core.InputContext;
using InputCue.Core.Settings;

namespace InputCue.Overlay;

internal static class OverlayPlacement
{
    internal static PixelPoint Calculate(
        ScreenRect anchor,
        PixelRect workArea,
        PixelSize overlaySize,
        int gap,
        IndicatorPlacement placement = IndicatorPlacement.Right,
        int horizontalOffset = 0,
        int verticalOffset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gap);
        if (!Enum.IsDefined(placement))
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }

        var position = CalculatePreferred(anchor, overlaySize, gap, placement);
        if (!Fits(position, workArea, overlaySize))
        {
            var opposite = CalculatePreferred(anchor, overlaySize, gap, Opposite(placement));
            if (Fits(opposite, workArea, overlaySize))
            {
                position = opposite;
            }
        }

        return new PixelPoint(
            Clamp(
                checked(position.X + horizontalOffset),
                workArea.Left,
                workArea.Right - overlaySize.Width),
            Clamp(
                checked(position.Y + verticalOffset),
                workArea.Top,
                workArea.Bottom - overlaySize.Height));
    }

    internal static int ScaleDipToPixels(double value, uint dpi) =>
        checked((int)Math.Round(value * dpi / 96d, MidpointRounding.AwayFromZero));

    private static int RoundToInt(double value) =>
        checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private static PixelPoint CalculatePreferred(
        ScreenRect anchor,
        PixelSize overlaySize,
        int gap,
        IndicatorPlacement placement)
    {
        var anchorLeft = RoundToInt(anchor.X);
        var anchorTop = RoundToInt(anchor.Y);
        var anchorRight = RoundToInt(anchor.X + anchor.Width);
        var anchorBottom = RoundToInt(anchor.Y + anchor.Height);
        var centeredX = RoundToInt(anchor.X + ((anchor.Width - overlaySize.Width) / 2));
        var centeredY = RoundToInt(anchor.Y + ((anchor.Height - overlaySize.Height) / 2));

        return placement switch
        {
            IndicatorPlacement.TopLeft => new(
                anchorLeft - gap - overlaySize.Width,
                anchorTop - gap - overlaySize.Height),
            IndicatorPlacement.Top => new(centeredX, anchorTop - gap - overlaySize.Height),
            IndicatorPlacement.TopRight => new(
                anchorRight + gap,
                anchorTop - gap - overlaySize.Height),
            IndicatorPlacement.Left => new(anchorLeft - gap - overlaySize.Width, centeredY),
            IndicatorPlacement.Right => new(anchorRight + gap, centeredY),
            IndicatorPlacement.BottomLeft => new(
                anchorLeft - gap - overlaySize.Width,
                anchorBottom + gap),
            IndicatorPlacement.Bottom => new(centeredX, anchorBottom + gap),
            IndicatorPlacement.BottomRight => new(anchorRight + gap, anchorBottom + gap),
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };
    }

    private static bool Fits(PixelPoint position, PixelRect workArea, PixelSize overlaySize) =>
        position.X >= workArea.Left &&
        position.Y >= workArea.Top &&
        position.X + overlaySize.Width <= workArea.Right &&
        position.Y + overlaySize.Height <= workArea.Bottom;

    private static IndicatorPlacement Opposite(IndicatorPlacement placement) => placement switch
    {
        IndicatorPlacement.TopLeft => IndicatorPlacement.BottomRight,
        IndicatorPlacement.Top => IndicatorPlacement.Bottom,
        IndicatorPlacement.TopRight => IndicatorPlacement.BottomLeft,
        IndicatorPlacement.Left => IndicatorPlacement.Right,
        IndicatorPlacement.Right => IndicatorPlacement.Left,
        IndicatorPlacement.BottomLeft => IndicatorPlacement.TopRight,
        IndicatorPlacement.Bottom => IndicatorPlacement.Top,
        IndicatorPlacement.BottomRight => IndicatorPlacement.TopLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(placement)),
    };

    private static int Clamp(int value, int minimum, int maximum) =>
        maximum < minimum ? minimum : Math.Clamp(value, minimum, maximum);
}

internal readonly record struct PixelPoint(int X, int Y);

internal readonly record struct PixelSize(int Width, int Height);

internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);
