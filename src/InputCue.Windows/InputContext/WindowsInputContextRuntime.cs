namespace InputCue.Windows.InputContext;

internal sealed class WindowsInputContextRuntime : IInputContextRuntime
{
    internal static WindowsInputContextRuntime Instance { get; } = new();

    private WindowsInputContextRuntime()
    {
    }

    public IInputContextEventSource CreateEventSource() =>
        WindowsInputContextEventSource.Create();

    public RawInputContextObservation Observe()
    {
        using var probe = new WindowsInputContextProbe();
        return probe.Observe();
    }

    public RawInputContextObservation RefreshInputState(RawInputContextObservation current) =>
        new WindowsInputStateRefreshProbe().Refresh(current);
}
