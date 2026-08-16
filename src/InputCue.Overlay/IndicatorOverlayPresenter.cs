using InputCue.Core.Indicator;

namespace InputCue.Overlay;

public sealed class IndicatorOverlayPresenter : IDisposable
{
    private readonly IndicatorOverlayWindow _window = new();
    private bool _disposed;

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
