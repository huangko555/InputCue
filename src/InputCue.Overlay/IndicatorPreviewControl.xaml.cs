using System.Windows.Controls;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;

namespace InputCue.Overlay;

public partial class IndicatorPreviewControl : UserControl
{
    private readonly IndicatorVisualRenderer _renderer;

    public IndicatorPreviewControl()
    {
        InitializeComponent();
        _renderer = new IndicatorVisualRenderer(
            IndicatorDot,
            BadgeVisual,
            BadgeShadow,
            BadgeBody,
            BadgeGlyphViewbox,
            BadgeGlyph,
            BadgeCustomIcon);
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
        _renderer.Configure(
            style,
            indicatorSizeDip,
            badgeSizeDip,
            chineseDotColor,
            englishDotColor,
            englishUsDotColor,
            capsLockDotColor);
        Width = _renderer.RootWidth;
        Height = _renderer.RootHeight;
    }

    public void UpdateCustomIcons(CustomIconImages images)
    {
        _renderer.UpdateCustomIcons(images);
    }

    public void UpdateCustomShadow(CustomIconShadowMode mode)
    {
        _renderer.UpdateCustomShadow(mode);
    }

    public void Render(InputState state)
    {
        _renderer.Render(state);
    }
}
