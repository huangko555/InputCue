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

        return false;
    }

    internal void Reset()
    {
        _lastEditableTarget = null;
        _candidate = null;
    }

    private sealed record ReturnCandidate(EditableTarget Target, DateTimeOffset LeftAt);

    private sealed record EditableTarget(
        nint ForegroundWindow,
        nint FocusWindow,
        int ProcessId,
        string ControlType,
        string ClassName,
        string FrameworkId,
        InputState InputState)
    {
        internal static EditableTarget From(
            RawInputContextObservation observation,
            InputState inputState) =>
            new(
                observation.ForegroundWindow,
                observation.FocusWindow,
                observation.Target.ProcessId,
                observation.Target.ControlType,
                observation.Target.ClassName,
                observation.Target.FrameworkId,
                inputState);

        internal bool SharesNativeContext(RawInputContextObservation observation) =>
            ForegroundWindow == observation.ForegroundWindow &&
            FocusWindow == observation.FocusWindow &&
            ProcessId == observation.Target.ProcessId;

        internal bool Matches(EditableTarget other) =>
            ForegroundWindow == other.ForegroundWindow &&
            FocusWindow == other.FocusWindow &&
            ProcessId == other.ProcessId &&
            ControlType == other.ControlType &&
            ClassName == other.ClassName &&
            FrameworkId == other.FrameworkId &&
            InputState == other.InputState;
    }
}
