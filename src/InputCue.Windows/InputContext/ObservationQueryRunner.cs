using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal sealed class ObservationQueryRunner : IDisposable
{
    private readonly TimeSpan _circuitCooldown;
    private readonly Func<RawInputContextObservation> _observe;
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private QueryWorker? _worker;
    private int _workerCreationCount;
    private long? _lastTimeoutTimestamp;
    private bool _disposed;

    internal int WorkerCreationCount => _workerCreationCount;

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
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation.ThrowIfCancellationRequested();
            ReapTimedOutWorker();

            if (_worker is { IsTimedOut: true } || IsCircuitOpen())
            {
                return RawInputContextObservation.Failure(
                    ProbeIssue.TimedOut,
                    _timeout.TotalMilliseconds);
            }

            _worker ??= CreateWorker();
            _worker.StartQuery();

            if (_worker.WaitForResult(_timeout))
            {
                return _worker.Observation ?? RawInputContextObservation.Failure(
                    ProbeIssue.SourceUnavailable,
                    _timeout.TotalMilliseconds);
            }

            _worker.MarkTimedOut();
            _lastTimeoutTimestamp = _timeProvider.GetTimestamp();
            return RawInputContextObservation.Failure(
                ProbeIssue.TimedOut,
                _timeout.TotalMilliseconds);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _disposed = true;
            _worker?.Dispose();
            _worker = null;
        }
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

    private QueryWorker CreateWorker()
    {
        _workerCreationCount++;
        return new QueryWorker(_observe);
    }

    private void ReapTimedOutWorker()
    {
        if (_worker is not { IsTimedOut: true, IsQueryCompleted: true } completedWorker)
        {
            return;
        }

        completedWorker.Dispose();
        _worker = null;
    }

    private sealed class QueryWorker : IDisposable
    {
        private readonly ManualResetEvent _completed = new(initialState: false);
        private readonly Func<RawInputContextObservation> _observe;
        private readonly AutoResetEvent _request = new(initialState: false);
        private readonly Thread _thread;
        private volatile bool _queryCompleted;
        private volatile bool _stopRequested;

        internal QueryWorker(Func<RawInputContextObservation> observe)
        {
            _observe = observe;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "InputCue.UIAutomation.Query",
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        internal bool IsQueryCompleted => _queryCompleted;

        internal bool IsTimedOut { get; private set; }

        internal RawInputContextObservation? Observation { get; private set; }

        internal void StartQuery()
        {
            Observation = null;
            IsTimedOut = false;
            _queryCompleted = false;
            _completed.Reset();
            _request.Set();
        }

        internal bool WaitForResult(TimeSpan timeout) => _completed.WaitOne(timeout);

        internal void MarkTimedOut() => IsTimedOut = true;

        public void Dispose() => RequestStop();

        private void RequestStop()
        {
            _stopRequested = true;
            _request.Set();
        }

        private void Run()
        {
            try
            {
                while (true)
                {
                    _request.WaitOne();
                    if (_stopRequested)
                    {
                        return;
                    }

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

                    _queryCompleted = true;
                    _completed.Set();
                    if (_stopRequested)
                    {
                        return;
                    }
                }
            }
            finally
            {
                _completed.Dispose();
                _request.Dispose();
            }
        }
    }
}
