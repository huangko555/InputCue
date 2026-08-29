using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;

namespace InputCue.Overlay;

/// <summary>
/// Applies indicator appearance and state to the shared visual elements hosted by
/// both the overlay window and the settings preview, so rendering decisions —
/// style layout, state glyphs, custom icon fallback — have a single implementation.
/// The host owns only its shell (window positioning or page layout) and applies
/// <see cref="RootWidth"/>/<see cref="RootHeight"/> after <see cref="Configure"/>.
/// </summary>
internal sealed class IndicatorVisualRenderer
{
    private const int WindowPaddingDip = 6;
    private const double SolidShadowOffsetDip = 5;    private const double BadgeBaseSizeDip = 36;
    private const double BadgeBaseBorderDip = 2.5;
    private const double BadgeBaseInsetDip = 5;
    private const double BadgeBaseShadowOffsetDip = 4;
    private const double SoftShadowPaddingDip = 5;

    private readonly Ellipse _dot;
    private readonly Canvas _badgeVisual;
    private readonly Border _badgeShadow;
    private readonly Border _badgeBody;
    private readonly Viewbox _badgeGlyphViewbox;
    private readonly Path _badgeGlyph;
    private readonly Image _customIconImage;

    private IndicatorStyle _style = IndicatorStyle.Dot;
    private int _dotSizeDip = InputCueSettings.DefaultIndicatorSizeDip;
    private int _badgeSizeDip = InputCueSettings.DefaultLightBadgeSizeDip;
    private Brush _chineseBrush = FrozenBrush(InputCueSettings.DefaultChineseDotColor);
    private Brush _englishBrush = FrozenBrush(InputCueSettings.DefaultEnglishDotColor);
    private Brush _englishUsBrush = FrozenBrush(InputCueSettings.DefaultEnglishUsDotColor);
    private Brush _capsLockBrush = FrozenBrush(InputCueSettings.DefaultCapsLockDotColor);
    private CustomIconImages _customIcons = CustomIconImages.Empty;
    private CustomIconShadowMode _customShadow = CustomIconShadowMode.None;

    public IndicatorVisualRenderer(
        Ellipse dot,
        Canvas badgeVisual,
        Border badgeShadow,
        Border badgeBody,
        Viewbox badgeGlyphViewbox,
        Path badgeGlyph,
        Image customIconImage)
    {
        _dot = dot;
        _badgeVisual = badgeVisual;
        _badgeShadow = badgeShadow;
        _badgeBody = badgeBody;
        _badgeGlyphViewbox = badgeGlyphViewbox;
        _badgeGlyph = badgeGlyph;
        _customIconImage = customIconImage;
    }

    public double RootWidth { get; private set; }

    public double RootHeight { get; private set; }

    public void Configure(
        IndicatorStyle style,
        int dotSizeDip,
        int badgeSizeDip,
        string chineseDotColor,
        string englishDotColor,
        string englishUsDotColor,
        string capsLockDotColor)
    {
        if (!Enum.IsDefined(style))
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }

        if (dotSizeDip is < InputCueSettings.MinimumIndicatorSizeDip or > InputCueSettings.MaximumIndicatorSizeDip)
        {
            throw new ArgumentOutOfRangeException(nameof(dotSizeDip));
        }

        if (badgeSizeDip is < InputCueSettings.MinimumLightBadgeSizeDip or > InputCueSettings.MaximumLightBadgeSizeDip)
        {
            throw new ArgumentOutOfRangeException(nameof(badgeSizeDip));
        }

        if (!InputCueSettings.IsHexColor(chineseDotColor) ||
            !InputCueSettings.IsHexColor(englishDotColor) ||
            !InputCueSettings.IsHexColor(englishUsDotColor) ||
            !InputCueSettings.IsHexColor(capsLockDotColor))
        {
            throw new ArgumentException("Dot colors must contain exactly six hexadecimal digits.");
        }

        _style = style;
        _dotSizeDip = dotSizeDip;
        _badgeSizeDip = badgeSizeDip;
        _chineseBrush = FrozenBrush(chineseDotColor);
        _englishBrush = FrozenBrush(englishDotColor);
        _englishUsBrush = FrozenBrush(englishUsDotColor);
        _capsLockBrush = FrozenBrush(capsLockDotColor);
        ApplyLayout();
    }

    public void UpdateCustomIcons(CustomIconImages images)
    {
        _customIcons = images ?? CustomIconImages.Empty;
    }

    public void UpdateCustomShadow(CustomIconShadowMode mode)
    {
        if (_customShadow == mode)
        {
            return;
        }

        _customShadow = mode;
        // Re-apply immediately so toggling never waits for the next Configure.
        ApplyLayout();
    }

    public void Render(InputState state)
    {
        var customImage = _style == IndicatorStyle.Custom
            ? _customIcons.For(state)
            : null;
        if (customImage is not null)
        {
            _customIconImage.Source = customImage;
            _customIconImage.Visibility = Visibility.Visible;
            _dot.Visibility = Visibility.Collapsed;
            _badgeVisual.Visibility = Visibility.Collapsed;
            return;
        }

        _customIconImage.Visibility = Visibility.Collapsed;
        _dot.Visibility = _style == IndicatorStyle.Dot
            ? Visibility.Visible
            : Visibility.Collapsed;
        _badgeVisual.Visibility = _style == IndicatorStyle.Dot
            ? Visibility.Collapsed
            : Visibility.Visible;

        _dot.Fill = state switch
        {
            InputState.Chinese => _chineseBrush,
            InputState.English => _englishBrush,
            InputState.EnglishUs => _englishUsBrush,
            InputState.CapsLock => _capsLockBrush,
            _ => Brushes.Transparent,
        };
        _badgeGlyph.Data = _style == IndicatorStyle.ShadowBadge
            ? ShadowBadgeGlyphs.For(state)
            : LightBadgeGlyphs.For(state);
        if (_style == IndicatorStyle.ShadowBadge)
        {
            _badgeGlyph.Width = 64;
            _badgeGlyph.Height = 64;
            _badgeGlyphViewbox.Margin = new Thickness(0);
            return;
        }

        _badgeGlyph.Width = double.NaN;
        _badgeGlyph.Height = double.NaN;
        var inset = state == InputState.EnglishUs
            ? BadgeBaseInsetDip * 0.85
            : BadgeBaseInsetDip;
        _badgeGlyphViewbox.Margin = new Thickness(inset * (_badgeSizeDip / BadgeBaseSizeDip));
    }

    /// <summary>
    /// Overall width and height the host must reserve for the styled indicator:
    /// dot plus padding, outlined badge plus its drop shadow, or soft card plus blur padding.
    /// </summary>
    internal static double CalculateRootSize(IndicatorStyle style, int dotSizeDip, int badgeSizeDip) => style switch
    {
        IndicatorStyle.Dot => dotSizeDip + WindowPaddingDip,
        IndicatorStyle.ShadowBadge => badgeSizeDip +
            (2 * SoftShadowPaddingDip * (badgeSizeDip / BadgeBaseSizeDip)),
        _ => badgeSizeDip + (BadgeBaseShadowOffsetDip * (badgeSizeDip / BadgeBaseSizeDip)),
    };

    private void ApplyLayout()
    {
        if (_style == IndicatorStyle.Dot)
        {
            _dot.Width = _dotSizeDip;
            _dot.Height = _dotSizeDip;
            RootWidth = CalculateRootSize(_style, _dotSizeDip, _badgeSizeDip);
            RootHeight = RootWidth;
            return;
        }

        var scale = _badgeSizeDip / BadgeBaseSizeDip;
        if (_style == IndicatorStyle.ShadowBadge)
        {
            var shadowPadding = SoftShadowPaddingDip * scale;
            var overallSize = CalculateRootSize(_style, _dotSizeDip, _badgeSizeDip);
            _badgeVisual.Width = overallSize;
            _badgeVisual.Height = overallSize;
            _badgeShadow.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(_badgeBody, shadowPadding);
            Canvas.SetTop(_badgeBody, shadowPadding);
            _badgeBody.Width = _badgeSizeDip;
            _badgeBody.Height = _badgeSizeDip;
            _badgeBody.Background = FrozenBrush("FCFCFC");
            _badgeBody.BorderBrush = FrozenBrush("D4D4D8");
            _badgeBody.BorderThickness = new Thickness(Math.Max(1, scale));
            _badgeBody.Effect = new DropShadowEffect
            {
                BlurRadius = 8 * scale,
                Color = Color.FromRgb(82, 82, 91),
                Direction = 0,
                Opacity = 0.22,
                ShadowDepth = 0,
            };
            _badgeGlyph.Fill = FrozenBrush("52525B");
            _badgeGlyphViewbox.Margin = new Thickness(0);
            RootWidth = overallSize;
            RootHeight = overallSize;
            return;
        }

        // LightBadge and the Custom-style fallback share the outlined-card layout.
        var shadowOffset = BadgeBaseShadowOffsetDip * scale;
        var outlinedOverallSize = CalculateRootSize(_style, _dotSizeDip, _badgeSizeDip);

        _badgeVisual.Width = outlinedOverallSize;
        _badgeVisual.Height = outlinedOverallSize;
        _badgeShadow.Visibility = Visibility.Visible;
        _badgeShadow.Width = _badgeSizeDip;
        _badgeShadow.Height = _badgeSizeDip;
        Canvas.SetLeft(_badgeShadow, shadowOffset);
        Canvas.SetTop(_badgeShadow, shadowOffset);
        Canvas.SetLeft(_badgeBody, 0);
        Canvas.SetTop(_badgeBody, 0);
        _badgeBody.Width = _badgeSizeDip;
        _badgeBody.Height = _badgeSizeDip;
        _badgeBody.Background = FrozenBrush("FFFFFF");
        _badgeBody.BorderBrush = FrozenBrush("000000");
        _badgeBody.BorderThickness = new Thickness(BadgeBaseBorderDip * scale);
        _badgeBody.Effect = null;
        _badgeGlyph.Fill = FrozenBrush("000000");
        _badgeGlyphViewbox.Margin = new Thickness(BadgeBaseInsetDip * scale);
        _customIconImage.Width = _badgeSizeDip;
        _customIconImage.Height = _badgeSizeDip;
        _customIconImage.Effect = _customShadow switch
        {
            CustomIconShadowMode.Light => SoftShadowEffect(0.32, scale),
            CustomIconShadowMode.Heavy => SoftShadowEffect(0.8, scale),
            CustomIconShadowMode.Solid => new DropShadowEffect
            {
                // A hard black copy of the bitmap itself, offset down-right by
                // exactly SolidShadowOffsetDip on both axes (315° splits the
                // depth evenly). Being the image's own shadow, it can never
                // mismatch the artwork's size or shape.
                BlurRadius = 0,
                Color = Colors.Black,
                Direction = 315,
                Opacity = 1,
                ShadowDepth = SolidShadowOffsetDip * Math.Sqrt(2),
            },
            _ => null,
        };
        RootWidth = outlinedOverallSize;
        RootHeight = outlinedOverallSize;
    }

    private static DropShadowEffect SoftShadowEffect(double opacity, double scale) => new()
    {
        BlurRadius = 7 * scale,
        Color = Colors.Black,
        Direction = 270,
        Opacity = opacity,
        ShadowDepth = 2 * scale,
    };

    private static SolidColorBrush FrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString($"#{color}"));
        brush.Freeze();
        return brush;
    }
}
