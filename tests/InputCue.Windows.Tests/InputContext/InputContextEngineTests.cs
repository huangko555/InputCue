using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class InputContextEngineTests
{
    [Fact]
    public async Task WatchAsyncPublishesTheObservedInputState()
    {
        var inputStateEvidence = new InputStateEvidence(0x0409, false, null, null, null);
        using var runtime = new TestInputContextRuntime(
            InputState.English,
            inputStateEvidence);
        var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(InputState.English, enumerator.Current.Snapshot.InputState);
        Assert.Equal(inputStateEvidence, enumerator.Current.InputStateEvidence);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncStopsWhenCancellationIsRequested()
    {
        using var runtime = new TestInputContextRuntime();
        var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Assert.True(await enumerator.MoveNextAsync());

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task WatchAsyncCoalescesChangesInsideTheDebounceWindow()
    {
        using var runtime = new TestInputContextRuntime();
        var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Assert.True(await enumerator.MoveNextAsync());

        runtime.SignalChange();
        Assert.True(runtime.ChangeWasObserved.Wait(TimeSpan.FromSeconds(1)));
        runtime.SignalChange();
        Assert.True(await enumerator.MoveNextAsync());

        var unexpected = enumerator.MoveNextAsync().AsTask();
        var completed = await Task.WhenAny(unexpected, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(unexpected, completed);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await unexpected);
    }

    [Fact]
    public async Task WatchAsyncKeepsOnlyTheLatestObservationForSlowConsumers()
    {
        using var runtime = new TestInputContextRuntime();
        var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Assert.True(await enumerator.MoveNextAsync());

        runtime.SignalChange();
        Assert.True(runtime.WaitForObservationCount(2, TimeSpan.FromSeconds(1)));
        runtime.SignalChange();
        Assert.True(runtime.WaitForObservationCount(3, TimeSpan.FromSeconds(1)));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("target-3", enumerator.Current.Target.ProcessName);
        Assert.Equal(3, enumerator.Current.Snapshot.Generation);
        cancellation.Cancel();
    }

    private sealed class TestInputContextRuntime : IInputContextRuntime, IDisposable
    {
        private readonly ManualResetEventSlim _changeWasObserved = new();
        private readonly AutoResetEvent _signal = new(initialState: false);
        private readonly InputState _inputState;
        private readonly InputStateEvidence _inputStateEvidence;
        private int _observationCount;

        internal TestInputContextRuntime(
            InputState inputState = InputState.Unknown,
            InputStateEvidence? inputStateEvidence = null)
        {
            _inputState = inputState;
            _inputStateEvidence = inputStateEvidence ?? InputStateEvidence.Unavailable;
        }

        internal ManualResetEventSlim ChangeWasObserved => _changeWasObserved;

        public IInputContextEventSource CreateEventSource() =>
            new TestEventSource(_signal, _changeWasObserved);

        public RawInputContextObservation Observe()
        {
            var identity = Interlocked.Increment(ref _observationCount);
            return new RawInputContextObservation(
                identity,
                identity,
                identity,
                new TargetDescriptor(identity, $"target-{identity}", "ControlType.Edit", "Edit", "Test"),
                _inputState,
                _inputStateEvidence,
                new InputEvidence(true, false, false, new ScreenRect(100, 120, 2, 20), null, null),
                UiAutomationCaretMethod.TextPattern,
                TextPattern2Status.PatternUnavailable,
                1);
        }

        internal void SignalChange() => _signal.Set();

        internal bool WaitForObservationCount(int expected, TimeSpan timeout) =>
            SpinWait.SpinUntil(
                () => Volatile.Read(ref _observationCount) >= expected,
                timeout);

        public void Dispose()
        {
            _changeWasObserved.Dispose();
            _signal.Dispose();
        }
    }

    private sealed class TestEventSource(
        AutoResetEvent signal,
        ManualResetEventSlim changeWasObserved) : IInputContextEventSource
    {
        public bool WaitForChange(TimeSpan fallbackInterval, CancellationToken cancellation)
        {
            var result = WaitHandle.WaitAny([signal, cancellation.WaitHandle], fallbackInterval);
            cancellation.ThrowIfCancellationRequested();
            if (result == 0)
            {
                changeWasObserved.Set();
            }

            return result == 0;
        }

        public void Dispose()
        {
        }
    }
}
