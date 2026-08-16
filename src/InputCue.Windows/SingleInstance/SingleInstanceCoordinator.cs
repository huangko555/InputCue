namespace InputCue.Windows.SingleInstance;

/// <summary>
/// Owns a per-session UI instance marker and forwards later launches as activation requests.
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly EventWaitHandle _activationEvent;
    private readonly Action _activationRequested;
    private readonly Mutex _instanceMarker;
    private readonly Thread _listener;
    private readonly AutoResetEvent _stopEvent = new(initialState: false);
    private bool _disposed;

    private SingleInstanceCoordinator(
        Mutex instanceMarker,
        EventWaitHandle activationEvent,
        Action activationRequested)
    {
        _instanceMarker = instanceMarker;
        _activationEvent = activationEvent;
        _activationRequested = activationRequested;
        _listener = new Thread(Listen)
        {
            IsBackground = true,
            Name = "InputCue.SingleInstance",
        };
        _listener.Start();
    }

    public static SingleInstanceCoordinator? TryAcquire(
        string instanceName,
        Action activationRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentNullException.ThrowIfNull(activationRequested);

        var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"Local\\{instanceName}.Activate");
        Mutex? instanceMarker = null;
        try
        {
            instanceMarker = new Mutex(
                initiallyOwned: false,
                $"Local\\{instanceName}.Marker",
                out var createdNew);
            if (!createdNew)
            {
                _ = activationEvent.Set();
                instanceMarker.Dispose();
                activationEvent.Dispose();
                return null;
            }

            return new SingleInstanceCoordinator(
                instanceMarker,
                activationEvent,
                activationRequested);
        }
        catch
        {
            instanceMarker?.Dispose();
            activationEvent.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = _stopEvent.Set();
        if (!_listener.Join(ShutdownTimeout))
        {
            return;
        }

        _stopEvent.Dispose();
        _activationEvent.Dispose();
        _instanceMarker.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Listen()
    {
        while (WaitHandle.WaitAny([_activationEvent, _stopEvent]) == 0)
        {
            try
            {
                _activationRequested();
            }
            catch
            {
                // A best-effort activation must never terminate the owning application.
            }
        }
    }
}
