using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal sealed class ContextReturnTracker
{
    private readonly TimeSpan _returnWindow;
    private EditableTarget? _lastEditableTarget;
    private ReturnCandidate? _candidate;

    internal ContextReturnTracker(TimeSpan returnWindow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(returnWindow, TimeSpan.Zero);
        _returnWindow = returnWindow;
    }

    internal bool IsContextLossPending => _candidate is not null;

    internal bool Observe(
        RawInputContextObservation observation,
        InputContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Eligibility is Eligibility.EditableCaret or Eligibility.EditableSelection)
        {
            var current = EditableTarget.From(observation, snapshot.InputState);
            var suppressReplay = _candidate is { } candidate &&
                snapshot.ObservedAt >= candidate.LeftAt &&
                snapshot.ObservedAt - candidate.LeftAt <= _returnWindow &&
                candidate.Target.Matches(current);
            _candidate = null;
            _lastEditableTarget = current;
            return suppressReplay;
        }

        var trackedTarget = _candidate?.Target ?? _lastEditableTarget;
        if (trackedTarget is not null && !trackedTarget.SharesNativeContext(observation))
        {
            Reset();
            return false;
        }

        if (snapshot.Eligibility is Eligibility.NoEditableFocus)
        {
            if (_candidate is not null)
            {
                return false;
            }

            if (_lastEditableTarget is { } lastEditableTarget &&
                lastEditableTarget.SharesNativeContext(observation))
            {
                _candidate = new ReturnCandidate(lastEditableTarget, snapshot.ObservedAt);
            }
            else
            {
                _lastEditableTarget = null;
            }
        }
        else if (snapshot.Eligibility is Eligibility.PositionUnknown &&
            _candidate is null &&
            _lastEditableTarget is { } lastEditableTarget &&
            lastEditableTarget.MatchesIgnoringInputState(observation))
        {
            _candidate = new ReturnCandidate(lastEditableTarget, snapshot.ObservedAt);
        }

        return false;
    }

    internal void Reset()
    {
        _lastEditableTarget = null;
        _candidate = null;
    }

    private sealed record ReturnCandidate(EditableTarget Target, DateTimeOffset LeftAt);

    private sealed record EditableTarget(
        RawInputContextObservation Observation,
        InputState InputState)
    {
        internal static EditableTarget From(
            RawInputContextObservation observation,
            InputState inputState) =>
            new(observation, inputState);

        internal bool SharesNativeContext(RawInputContextObservation observation) =>
            Observation.ForegroundWindow == observation.ForegroundWindow &&
            Observation.FocusWindow == observation.FocusWindow &&
            Observation.Target.ProcessId == observation.Target.ProcessId;

        internal bool Matches(EditableTarget other) =>
            InputTargetContinuity.IsSameTarget(other.Observation, Observation) &&
            InputState == other.InputState;

        internal bool MatchesIgnoringInputState(RawInputContextObservation other) =>
            InputTargetContinuity.IsSameTarget(other, Observation);
    }
}
