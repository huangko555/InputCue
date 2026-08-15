namespace InputCue.Windows.InputContext;

internal interface IInputContextRuntime
{
    public IInputContextEventSource CreateEventSource();

    public RawInputContextObservation Observe();
}
