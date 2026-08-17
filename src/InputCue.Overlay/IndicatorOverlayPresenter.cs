using InputCue.Core.Indicator;
using InputCue.Core.Settings;

namespace InputCue.Overlay;

public sealed class IndicatorOverlayPresenter : IDisposable
{
    private readonly IndicatorOverlayWindow _window = new();
    private bool _disposed;

    public void Configure(
        IndicatorStyle style,
        IndicatorPlacement placement,
        int horizontalOffsetDip,
        int verticalOffsetDip,
        int indicatorSizeDip,
        int lightBadgeSizeDip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Configure(
            style,
            placement,
            horizontalOffsetDip,
            verticalOffsetDip,
            indicatorSizeDip,
            lightBadgeSizeDip);
    }

    public void Update(IndicatorViewState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);
        _window.Render(state);
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Hide();
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
