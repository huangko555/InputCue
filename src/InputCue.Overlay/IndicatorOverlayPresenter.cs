using InputCue.Core.Indicator;
using InputCue.Core.Settings;

namespace InputCue.Overlay;

public sealed class IndicatorOverlayPresenter : IDisposable
{
    private readonly IndicatorOverlayWindow _window;
    private bool _disposed;

    public IndicatorOverlayPresenter()
    {
        _window = new IndicatorOverlayWindow();
    }

    public void Configure(
        IndicatorStyle style,
        IndicatorPlacement placement,
        int horizontalOffsetDip,
        int verticalOffsetDip,
        int indicatorSizeDip,
        int lightBadgeSizeDip,
        IndicatorTransitionAnimation transitionAnimation,
        string chineseDotColor,
        string englishDotColor,
        string englishUsDotColor,
        string capsLockDotColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Configure(
            style,
            placement,
            horizontalOffsetDip,
            verticalOffsetDip,
            indicatorSizeDip,
            lightBadgeSizeDip,
            transitionAnimation,
            chineseDotColor,
            englishDotColor,
            englishUsDotColor,
            capsLockDotColor);
    }

    public void UpdateCustomIcons(CustomIconImages images)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(images);
        _window.UpdateCustomIcons(images);
    }

    public void UpdateCustomShadow(CustomIconShadowMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.UpdateCustomShadow(mode);
    }

    public void Update(
        IndicatorViewState state,
        bool targetChanged = false,
        bool contextActivated = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);
        _window.Render(state, targetChanged, contextActivated);
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.HideIndicator();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.Close();
    }
}
