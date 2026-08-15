namespace InputCue.Core.InputContext;

public sealed class InputContextTraceBuffer
{
    private readonly int _capacity;
    private readonly List<InputContextDiagnostic> _observations = [];
    private InputContextDiagnostic? _lastSemanticObservation;

    public InputContextTraceBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public IReadOnlyList<InputContextDiagnostic> Observations => _observations;

    public void Add(InputContextDiagnostic observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var semanticObservation = SemanticObservation(observation);
        if (semanticObservation == _lastSemanticObservation)
        {
            _observations[^1] = observation;
            return;
        }

        _observations.Add(observation);
        _lastSemanticObservation = semanticObservation;
        if (_observations.Count > _capacity)
        {
            _observations.RemoveAt(0);
        }
    }

    private static InputContextDiagnostic SemanticObservation(
        InputContextDiagnostic observation) => observation with
        {
            Snapshot = observation.Snapshot with
            {
                ObservedAt = DateTimeOffset.UnixEpoch,
            },
            DurationMilliseconds = 0,
        };
}
