using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InputCue.Overlay;

/// <summary>
/// Draws a superellipse-style border whose curvature eases continuously into
/// its straight edges. The current indicator uses uniform radii and thickness.
/// </summary>
public sealed class ContinuousCornerBorder : Border
{
    private const double SuperellipseExponent = 4;
    private const int SamplesPerCorner = 12;

    protected override void OnRender(DrawingContext dc)
    {
        if (!IsUniform(BorderThickness) || !IsUniform(CornerRadius))
        {
            base.OnRender(dc);
            return;
        }

        var thickness = BorderBrush is null ? 0 : BorderThickness.Left;
        var pen = thickness > 0 ? new Pen(BorderBrush, thickness) : null;
        var halfStroke = thickness / 2;
        var bounds = new Rect(
            halfStroke,
            halfStroke,
            Math.Max(0, ActualWidth - thickness),
            Math.Max(0, ActualHeight - thickness));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var radius = Math.Clamp(
            CornerRadius.TopLeft - halfStroke,
            0,
            Math.Min(bounds.Width, bounds.Height) / 2);
        var geometry = CreateGeometry(bounds, radius);
        dc.DrawGeometry(Background, pen, geometry);
    }

    internal static StreamGeometry CreateGeometry(Rect bounds, double radius)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        var clampedRadius = Math.Clamp(
            radius,
            0,
            Math.Min(bounds.Width, bounds.Height) / 2);

        using (var context = geometry.Open())
        {
            context.BeginFigure(
                new Point(bounds.Left + clampedRadius, bounds.Top),
                isFilled: true,
                isClosed: true);
            context.LineTo(
                new Point(bounds.Right - clampedRadius, bounds.Top),
                isStroked: true,
                isSmoothJoin: false);
            AddCorner(context, bounds.Right - clampedRadius, bounds.Top + clampedRadius, clampedRadius, -90, 0);
            context.LineTo(
                new Point(bounds.Right, bounds.Bottom - clampedRadius),
                isStroked: true,
                isSmoothJoin: false);
            AddCorner(context, bounds.Right - clampedRadius, bounds.Bottom - clampedRadius, clampedRadius, 0, 90);
            context.LineTo(
                new Point(bounds.Left + clampedRadius, bounds.Bottom),
                isStroked: true,
                isSmoothJoin: false);
            AddCorner(context, bounds.Left + clampedRadius, bounds.Bottom - clampedRadius, clampedRadius, 90, 180);
            context.LineTo(
                new Point(bounds.Left, bounds.Top + clampedRadius),
                isStroked: true,
                isSmoothJoin: false);
            AddCorner(context, bounds.Left + clampedRadius, bounds.Top + clampedRadius, clampedRadius, 180, 270);
        }

        geometry.Freeze();
        return geometry;
    }

    private static void AddCorner(
        StreamGeometryContext context,
        double centerX,
        double centerY,
        double radius,
        double startDegrees,
        double endDegrees)
    {
        if (radius <= 0)
        {
            return;
        }

        var points = new List<Point>(SamplesPerCorner);
        var power = 2 / SuperellipseExponent;
        for (var index = 1; index <= SamplesPerCorner; index++)
        {
            var degrees = startDegrees + ((endDegrees - startDegrees) * index / SamplesPerCorner);
            var radians = degrees * Math.PI / 180;
            var cosine = Math.Cos(radians);
            var sine = Math.Sin(radians);
            points.Add(new Point(
                centerX + (radius * Math.Sign(cosine) * Math.Pow(Math.Abs(cosine), power)),
                centerY + (radius * Math.Sign(sine) * Math.Pow(Math.Abs(sine), power))));
        }

        context.PolyLineTo(points, isStroked: true, isSmoothJoin: true);
    }

    private static bool IsUniform(Thickness thickness) =>
        thickness.Left == thickness.Top &&
        thickness.Left == thickness.Right &&
        thickness.Left == thickness.Bottom;

    private static bool IsUniform(CornerRadius radius) =>
        radius.TopLeft == radius.TopRight &&
        radius.TopLeft == radius.BottomRight &&
        radius.TopLeft == radius.BottomLeft;
}
