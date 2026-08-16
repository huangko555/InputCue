using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class ObservationQueryRunnerTests
{
    [Fact]
    public void ObserveReturnsCompletedObservation()
    {
        using var runner = new ObservationQueryRunner(
            SuccessfulObservation,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2));

        var result = runner.Observe(CancellationToken.None);

        Assert.Equal(ProbeIssue.None, result.Evidence.Issue);
    }

    [Fact]
    public void CompletedQueriesReuseOneWorkerThread()
    {
        using var runner = new ObservationQueryRunner(
            SuccessfulObservation,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2));
        for (var query = 0; query < 100; query++)
        {
            _ = runner.Observe(CancellationToken.None);
        }

        Assert.Equal(1, runner.WorkerCreationCount);
    }

    [Fact]
    public void ObserveDoesNotStartAnotherQueryWhileTimedOutQueryIsStillRunning()
    {
        using var releaseQuery = new ManualResetEventSlim();
        using var queryFinished = new ManualResetEventSlim();
        var invocationCount = 0;
        using var runner = new ObservationQueryRunner(
            () =>
            {
                Interlocked.Increment(ref invocationCount);
                releaseQuery.Wait();
                queryFinished.Set();
                return SuccessfulObservation();
            },
            TimeSpan.FromMilliseconds(50),
            TimeSpan.Zero);

        var first = runner.Observe(CancellationToken.None);
        var second = runner.Observe(CancellationToken.None);

        Assert.Equal(ProbeIssue.TimedOut, first.Evidence.Issue);
        Assert.Equal(ProbeIssue.TimedOut, second.Evidence.Issue);
        Assert.Equal(1, Volatile.Read(ref invocationCount));

        releaseQuery.Set();
        Assert.True(queryFinished.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ObserveRetriesOnlyAfterTimedOutQueryFinishesAndCooldownExpires()
    {
        using var releaseFirstQuery = new ManualResetEventSlim();
        using var firstQueryFinished = new ManualResetEventSlim();
        var invocationCount = 0;
        var timeProvider = new ManualTimeProvider();
        using var runner = new ObservationQueryRunner(
            () =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    releaseFirstQuery.Wait();
                    firstQueryFinished.Set();
                }

                return SuccessfulObservation();
            },
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(2),
            timeProvider);

        var timedOut = runner.Observe(CancellationToken.None);
        releaseFirstQuery.Set();
        Assert.True(firstQueryFinished.Wait(TimeSpan.FromSeconds(2)));

        var whileCoolingDown = runner.Observe(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var recovered = runner.Observe(CancellationToken.None);

        Assert.Equal(ProbeIssue.TimedOut, timedOut.Evidence.Issue);
        Assert.Equal(ProbeIssue.TimedOut, whileCoolingDown.Evidence.Issue);
        Assert.Equal(ProbeIssue.None, recovered.Evidence.Issue);
        Assert.Equal(2, Volatile.Read(ref invocationCount));
    }

    private static RawInputContextObservation SuccessfulObservation() =>
        RawInputContextObservation.Failure(ProbeIssue.None, durationMilliseconds: 1);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        internal void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _timestamp, duration.Ticks);
    }
}
