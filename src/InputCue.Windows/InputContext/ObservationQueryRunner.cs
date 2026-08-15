using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal sealed class ObservationQueryRunner
{
    private readonly TimeSpan _circuitCooldown;
    private readonly Func<RawInputContextObservation> _observe;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private QueryExecution? _inFlight;
    private long? _lastTimeoutTimestamp;

    internal ObservationQueryRunner(
        Func<RawInputContextObservation> observe,
        TimeSpan timeout,
        TimeSpan circuitCooldown,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(observe);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(circuitCooldown, TimeSpan.Zero);

        _observe = observe;
        _timeout = timeout;
        _circuitCooldown = circuitCooldown;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal RawInputContextObservation Observe(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ReapCompletedQuery();

        if (_inFlight is not null || IsCircuitOpen())
        {
            return RawInputContextObservation.Failure(
                ProbeIssue.TimedOut,
                _timeout.TotalMilliseconds);
        }

        var execution = new QueryExecution(_observe);
        execution.Thread.Start();

        if (execution.Thread.Join(_timeout))
        {
            return execution.Observation ?? RawInputContextObservation.Failure(
                ProbeIssue.SourceUnavailable,
                _timeout.TotalMilliseconds);
        }

        _inFlight = execution;
        _lastTimeoutTimestamp = _timeProvider.GetTimestamp();
        return RawInputContextObservation.Failure(
            ProbeIssue.TimedOut,
            _timeout.TotalMilliseconds);
    }

    private bool IsCircuitOpen()
    {
        if (_lastTimeoutTimestamp is not { } lastTimeout)
        {
            return false;
        }

        var elapsed = _timeProvider.GetElapsedTime(lastTimeout, _timeProvider.GetTimestamp());
        if (elapsed < _circuitCooldown)
        {
            return true;
        }

        _lastTimeoutTimestamp = null;
        return false;
    }

    private void ReapCompletedQuery()
    {
        if (_inFlight is null || _inFlight.Thread.IsAlive)
        {
            return;
        }

        _inFlight.Thread.Join();
        _inFlight = null;
    }

    private sealed class QueryExecution
    {
        private readonly Func<RawInputContextObservation> _observe;

        internal QueryExecution(Func<RawInputContextObservation> observe)
        {
            _observe = observe;
            Thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "InputCue.UIAutomation.Query",
            };
            Thread.SetApartmentState(ApartmentState.MTA);
        }

        internal RawInputContextObservation? Observation { get; private set; }

        internal Thread Thread { get; }

        private void Run()
        {
            try
            {
                Observation = _observe();
            }
            catch (Exception exception) when (exception is not StackOverflowException)
            {
                Observation = RawInputContextObservation.Failure(
                    ProbeIssue.SourceUnavailable,
                    durationMilliseconds: 0);
            }
        }
    }
}
