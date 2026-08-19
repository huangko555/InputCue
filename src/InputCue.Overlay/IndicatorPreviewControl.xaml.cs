using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;

namespace InputCue.Overlay;

public partial class IndicatorPreviewControl : UserControl
{
    private const double WindowPaddingDip = 6;
    private const double BadgeBaseSizeDip = 36;
    private const double BadgeBaseBorderDip = 2.5;
    private const double BadgeBaseInsetDip = 5;
    private const double BadgeBaseShadowOffsetDip = 4;
    private const double SoftShadowPaddingDip = 5;

    private IndicatorStyle _style;
    private int _badgeSizeDip = InputCueSettings.DefaultLightBadgeSizeDip;
    private Brush _chineseBrush = FrozenBrush(InputCueSettings.DefaultChineseDotColor);
    private Brush _englishBrush = FrozenBrush(InputCueSettings.DefaultEnglishDotColor);
    private Brush _englishUsBrush = FrozenBrush(InputCueSettings.DefaultEnglishUsDotColor);
    private Brush _capsLockBrush = FrozenBrush(InputCueSettings.DefaultCapsLockDotColor);

    public IndicatorPreviewControl()
    {
        InitializeComponent();
    }

    public void Configure(
        IndicatorStyle style,
        int indicatorSizeDip,
        int badgeSizeDip,
        string chineseDotColor,
        string englishDotColor,
        string englishUsDotColor,
        string capsLockDotColor)
    {
        _style = style;
        _badgeSizeDip = badgeSizeDip;
        _chineseBrush = FrozenBrush(chineseDotColor);
        _englishBrush = FrozenBrush(englishDotColor);
        _englishUsBrush = FrozenBrush(englishUsDotColor);
        _capsLockBrush = FrozenBrush(capsLockDotColor);
        ConfigureVisuals(indicatorSizeDip, badgeSizeDip);
    }

    public void Render(InputState state)
    {
        IndicatorDot.Fill = state switch
        {
            InputState.Chinese => _chineseBrush,
            InputState.English => _englishBrush,
            InputState.EnglishUs => _englishUsBrush,
            InputState.CapsLock => _capsLockBrush,
            _ => Brushes.Transparent,
        };
        BadgeGlyph.Data = _style == IndicatorStyle.ShadowBadge
            ? ShadowBadgeGlyphs.For(state)
            : LightBadgeGlyphs.For(state);

        if (_style == IndicatorStyle.ShadowBadge)
        {
            BadgeGlyph.Width = 64;
            BadgeGlyph.Height = 64;
            BadgeGlyphViewbox.Margin = new Thickness(0);
            return;
        }

        BadgeGlyph.Width = double.NaN;
        BadgeGlyph.Height = double.NaN;
        var inset = state == InputState.EnglishUs ? BadgeBaseInsetDip * 0.85 : BadgeBaseInsetDip;
        BadgeGlyphViewbox.Margin = new Thickness(inset * (_badgeSizeDip / BadgeBaseSizeDip));
    }

    private void ConfigureVisuals(int indicatorSizeDip, int badgeSizeDip)
    {
        IndicatorDot.Visibility = _style == IndicatorStyle.Dot
            ? Visibility.Visible
            : Visibility.Collapsed;
        BadgeVisual.Visibility = _style is IndicatorStyle.LightBadge or IndicatorStyle.ShadowBadge
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_style == IndicatorStyle.Dot)
        {
            IndicatorDot.Width = indicatorSizeDip;
            IndicatorDot.Height = indicatorSizeDip;
            Width = indicatorSizeDip + WindowPaddingDip;
            Height = indicatorSizeDip + WindowPaddingDip;
            return;
        }

        var scale = badgeSizeDip / BadgeBaseSizeDip;
        if (_style == IndicatorStyle.ShadowBadge)
        {
            var padding = SoftShadowPaddingDip * scale;
            var overallSize = badgeSizeDip + (padding * 2);
            BadgeVisual.Width = overallSize;
            BadgeVisual.Height = overallSize;
            BadgeShadow.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(BadgeBody, padding);
            Canvas.SetTop(BadgeBody, padding);
            BadgeBody.Width = badgeSizeDip;
            BadgeBody.Height = badgeSizeDip;
            BadgeBody.Background = FrozenBrush("FCFCFC");
            BadgeBody.BorderBrush = FrozenBrush("D4D4D8");
            BadgeBody.BorderThickness = new Thickness(Math.Max(1, scale));
            BadgeBody.Effect = new DropShadowEffect
            {
                BlurRadius = 8 * scale,
                Color = Color.FromRgb(82, 82, 91),
                Direction = 0,
                Opacity = 0.22,
                ShadowDepth = 0,
            };
            BadgeGlyph.Fill = FrozenBrush("52525B");
            BadgeGlyphViewbox.Margin = new Thickness(0);
            Width = overallSize;
            Height = overallSize;
            return;
        }

        var shadowOffset = BadgeBaseShadowOffsetDip * scale;
        var outlinedSize = badgeSizeDip + shadowOffset;
        BadgeVisual.Width = outlinedSize;
        BadgeVisual.Height = outlinedSize;
        BadgeShadow.Visibility = Visibility.Visible;
        BadgeShadow.Width = badgeSizeDip;
        BadgeShadow.Height = badgeSizeDip;
        Canvas.SetLeft(BadgeShadow, shadowOffset);
        Canvas.SetTop(BadgeShadow, shadowOffset);
        Canvas.SetLeft(BadgeBody, 0);
        Canvas.SetTop(BadgeBody, 0);
        BadgeBody.Width = badgeSizeDip;
        BadgeBody.Height = badgeSizeDip;
        BadgeBody.Background = FrozenBrush("FFFFFF");
        BadgeBody.BorderBrush = FrozenBrush("000000");
        BadgeBody.BorderThickness = new Thickness(BadgeBaseBorderDip * scale);
        BadgeBody.Effect = null;
        BadgeGlyph.Fill = FrozenBrush("000000");
        BadgeGlyphViewbox.Margin = new Thickness(BadgeBaseInsetDip * scale);
        Width = outlinedSize;
        Height = outlinedSize;
    }

    private static SolidColorBrush FrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString($"#{color}"));
        brush.Freeze();
        return brush;
    }
}
