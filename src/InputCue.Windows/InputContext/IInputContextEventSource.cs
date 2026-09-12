namespace InputCue.Windows.InputContext;

internal interface IInputContextEventSource : IDisposable
{
    public bool WaitForChange(TimeSpan fallbackInterval, CancellationToken cancellation);

    public void SignalChange();
}
