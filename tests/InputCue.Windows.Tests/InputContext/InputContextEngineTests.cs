using System.Diagnostics;
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
        using var engine = new InputContextEngine(
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
    public async Task WatchAsyncRefreshesInputStateWithinInteractiveBudgetWithoutEvent()
    {
        using var runtime = new MutableInputStateRuntime(InputState.Chinese);
        using var engine = new InputContextEngine(
            sampleInterval: null,
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(InputState.Chinese, enumerator.Current.Snapshot.InputState);

        runtime.SetInputState(InputState.English);
        var refreshed = enumerator.MoveNextAsync().AsTask();
        var completed = await Task.WhenAny(
            refreshed,
            Task.Delay(TimeSpan.FromMilliseconds(300)));
        if (completed != refreshed)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await refreshed);
        }

        Assert.Same(refreshed, completed);
        Assert.True(await refreshed);
        Assert.Equal(InputState.English, enumerator.Current.Snapshot.InputState);
        Assert.InRange(runtime.FullObservationCount, 1, 4);
        Assert.True(runtime.FullObservationCount > 1 || runtime.InputStateRefreshCount >= 1);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncDoesNotFastRefreshWithoutEditableFocus()
    {
        using var runtime = new MutableInputStateRuntime(
            InputState.Unknown,
            hasEditableFocus: false);
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        Assert.Equal(1, runtime.FullObservationCount);
        Assert.Equal(0, runtime.InputStateRefreshCount);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncRefreshesInputStateWhenEditablePositionIsUnknown()
    {
        using var runtime = new MutableInputStateRuntime(
            InputState.Chinese,
            hasCaret: false);
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(Eligibility.PositionUnknown, enumerator.Current.Snapshot.Eligibility);
        Assert.True(SpinWait.SpinUntil(
            () => runtime.FullObservationCount == 4,
            TimeSpan.FromMilliseconds(400)));
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(InputState.Chinese, enumerator.Current.Snapshot.InputState);

        runtime.SetInputState(InputState.English);
        using var refreshTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (enumerator.Current.Snapshot.InputState is not InputState.English)
        {
            Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(refreshTimeout.Token));
        }

        Assert.Equal(InputState.English, enumerator.Current.Snapshot.InputState);
        Assert.Equal(4, runtime.FullObservationCount);
        Assert.True(runtime.InputStateRefreshCount >= 1);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncRetriesARecentlyFocusedEditablePosition()
    {
        using var runtime = new MutableInputStateRuntime(
            InputState.English,
            caretAvailableAfterObservation: 2);
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        if (enumerator.Current.Snapshot.Eligibility is Eligibility.PositionUnknown)
        {
            Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
        }

        Assert.Equal(Eligibility.EditableCaret, enumerator.Current.Snapshot.Eligibility);
        Assert.InRange(runtime.FullObservationCount, 2, 4);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncMarksBoundedPositionStabilizationObservations()
    {
        using var runtime = new MutableInputStateRuntime(
            InputState.English,
            caretXAfterFirstObservation: 145);
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.False(enumerator.Current.IsPositionStabilization);
        Assert.Equal(100, enumerator.Current.Snapshot.Anchor?.X);

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(300)));
        Assert.True(enumerator.Current.IsPositionStabilization);
        Assert.Equal(145, enumerator.Current.Snapshot.Anchor?.X);
        Assert.True(SpinWait.SpinUntil(
            () => runtime.FullObservationCount == 4,
            TimeSpan.FromMilliseconds(400)));
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncMarksAQuickReturnToTheSameEditorForReplaySuppression()
    {
        using var runtime = new ContextReturnRuntime();
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.False(enumerator.Current.SuppressContextReplay);

        runtime.Advance();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(300)));
        Assert.Equal(Eligibility.NoEditableFocus, enumerator.Current.Snapshot.Eligibility);

        runtime.Advance();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(300)));
        Assert.Equal(Eligibility.EditableCaret, enumerator.Current.Snapshot.Eligibility);
        Assert.True(enumerator.Current.SuppressContextReplay);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncSuppressesTheObservedCodexEditorReturnDelay()
    {
        using var runtime = new ContextReturnRuntime();
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        runtime.Advance();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(300)));
        Assert.Equal(Eligibility.NoEditableFocus, enumerator.Current.Snapshot.Eligibility);

        await Task.Delay(TimeSpan.FromMilliseconds(950));
        runtime.Advance();

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(300)));
        Assert.Equal(Eligibility.EditableCaret, enumerator.Current.Snapshot.Eligibility);
        Assert.True(enumerator.Current.SuppressContextReplay);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncBoundsPositionRetriesWhenNoCaretAppears()
    {
        using var runtime = new MutableInputStateRuntime(
            InputState.English,
            hasCaret: false);
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        await Task.Delay(TimeSpan.FromMilliseconds(450));

        Assert.Equal(4, runtime.FullObservationCount);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncStopsWhenCancellationIsRequested()
    {
        using var runtime = new TestInputContextRuntime();
        using var engine = new InputContextEngine(
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
    public async Task WatchAsyncFallsBackToObservationWhenNoChangeEventArrives()
    {
        using var runtime = new TestInputContextRuntime();
        using var engine = new InputContextEngine(
            TimeSpan.FromMilliseconds(50),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("target-1", enumerator.Current.Target.ProcessName);

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(300)));
        Assert.Equal("target-2", enumerator.Current.Target.ProcessName);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncCanRestartAfterRepeatedCancellation()
    {
        using var runtime = new TestInputContextRuntime();
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);

        for (var cycle = 0; cycle < 10; cycle++)
        {
            using var cancellation = new CancellationTokenSource();
            await using var enumerator = engine
                .WatchAsync(cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);

            Assert.True(await enumerator.MoveNextAsync());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        }

        Assert.Equal(10, runtime.ObservationCount);
        Assert.Equal(1, engine.QueryWorkerCreationCount);
    }

    [Fact]
    public async Task WatchAsyncCanRestartAfterAnUnexpectedEventSourceFailure()
    {
        using var runtime = new TransientEventSourceFailureRuntime();
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);

        await using (var failed = engine.WatchAsync().GetAsyncEnumerator())
        {
            Assert.True(await failed.MoveNextAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await failed.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        }

        using var cancellation = new CancellationTokenSource();
        await using var recovered = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await recovered.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, runtime.EventSourceCreationCount);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncCoalescesChangesInsideTheDebounceWindow()
    {
        using var runtime = new TestInputContextRuntime(stableIdentity: true);
        using var engine = new InputContextEngine(
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
    public async Task WatchAsyncBoundsFullObservationsDuringAnEventStorm()
    {
        using var runtime = new TestInputContextRuntime();
        using var engine = new InputContextEngine(
            TimeSpan.FromSeconds(5),
            ignoreCurrentProcess: false,
            runtime);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = engine
            .WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Assert.True(await enumerator.MoveNextAsync());

        var storm = Task.Run(() =>
        {
            var startedAt = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromMilliseconds(360))
            {
                runtime.SignalChange();
                Thread.Sleep(2);
            }
        });
        await storm;
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        Assert.InRange(runtime.ObservationCount, 2, 5);
        cancellation.Cancel();
    }

    [Fact]
    public async Task WatchAsyncKeepsOnlyTheLatestObservationForSlowConsumers()
    {
        using var runtime = new TestInputContextRuntime();
        using var engine = new InputContextEngine(
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
        private readonly bool _stableIdentity;
        private int _observationCount;

        internal TestInputContextRuntime(
            InputState inputState = InputState.Unknown,
            InputStateEvidence? inputStateEvidence = null,
            bool stableIdentity = false)
        {
            _inputState = inputState;
            _inputStateEvidence = inputStateEvidence ?? InputStateEvidence.Unavailable;
            _stableIdentity = stableIdentity;
        }

        internal ManualResetEventSlim ChangeWasObserved => _changeWasObserved;

        internal int ObservationCount => Volatile.Read(ref _observationCount);

        public IInputContextEventSource CreateEventSource() =>
            new TestEventSource(_signal, _changeWasObserved);

        public RawInputContextObservation Observe()
        {
            var observationCount = Interlocked.Increment(ref _observationCount);
            var identity = _stableIdentity ? 1 : observationCount;
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

        public RawInputContextObservation RefreshInputState(RawInputContextObservation current) =>
            current;

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

    private sealed class ContextReturnRuntime : IInputContextRuntime, IDisposable
    {
        private readonly ManualResetEventSlim _changeWasObserved = new();
        private readonly AutoResetEvent _signal = new(initialState: false);
        private int _state;

        public IInputContextEventSource CreateEventSource() =>
            new TestEventSource(_signal, _changeWasObserved);

        public RawInputContextObservation Observe()
        {
            var state = Volatile.Read(ref _state);
            var isEditable = state is not 1;
            return new RawInputContextObservation(
                10,
                20,
                state + 1,
                new TargetDescriptor(
                    30,
                    "browser",
                    isEditable ? "ControlType.Edit" : "ControlType.Button",
                    string.Empty,
                    "Chrome"),
                InputState.English,
                InputStateEvidence.Unavailable,
                new InputEvidence(
                    isEditable,
                    false,
                    false,
                    isEditable ? new ScreenRect(100, 120, 2, 20) : null,
                    null,
                    null),
                UiAutomationCaretMethod.TextPattern,
                TextPattern2Status.PatternUnavailable,
                1);
        }

        public RawInputContextObservation RefreshInputState(RawInputContextObservation current) =>
            current;

        internal void Advance()
        {
            _ = Interlocked.Increment(ref _state);
            _signal.Set();
        }

        public void Dispose()
        {
            _changeWasObserved.Dispose();
            _signal.Dispose();
        }
    }

    private sealed class MutableInputStateRuntime(
        InputState initialState,
        bool hasEditableFocus = true,
        bool hasCaret = true,
        int caretAvailableAfterObservation = 1,
        double caretXAfterFirstObservation = 100) :
        IInputContextRuntime,
        IDisposable
    {
        private readonly ManualResetEventSlim _changeWasObserved = new();
        private readonly AutoResetEvent _signal = new(initialState: false);
        private int _fullObservationCount;
        private int _inputState = (int)initialState;
        private int _inputStateRefreshCount;

        internal int FullObservationCount => Volatile.Read(ref _fullObservationCount);

        internal int InputStateRefreshCount => Volatile.Read(ref _inputStateRefreshCount);

        public IInputContextEventSource CreateEventSource() =>
            new TestEventSource(_signal, _changeWasObserved);

        public RawInputContextObservation Observe()
        {
            var observationCount = Interlocked.Increment(ref _fullObservationCount);
            var inputState = (InputState)Volatile.Read(ref _inputState);
            return new RawInputContextObservation(
                1,
                1,
                1,
                new TargetDescriptor(1, "target", "ControlType.Edit", "Edit", "Test"),
                inputState,
                InputStateEvidence.Unavailable,
                new InputEvidence(
                    hasEditableFocus,
                    false,
                    false,
                    hasEditableFocus &&
                    hasCaret &&
                    observationCount >= caretAvailableAfterObservation
                        ? new ScreenRect(
                            observationCount == 1 ? 100 : caretXAfterFirstObservation,
                            120,
                            2,
                            20)
                        : null,
                    null,
                    null),
                UiAutomationCaretMethod.TextPattern,
                TextPattern2Status.PatternUnavailable,
                1);
        }

        public RawInputContextObservation RefreshInputState(RawInputContextObservation current)
        {
            _ = Interlocked.Increment(ref _inputStateRefreshCount);
            return current with
            {
                InputState = (InputState)Volatile.Read(ref _inputState),
                DurationMilliseconds = 1,
            };
        }

        internal void SetInputState(InputState inputState) =>
            Volatile.Write(ref _inputState, (int)inputState);

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

    private sealed class TransientEventSourceFailureRuntime : IInputContextRuntime, IDisposable
    {
        private readonly ManualResetEventSlim _changeWasObserved = new();
        private readonly AutoResetEvent _signal = new(initialState: false);
        private int _eventSourceCreationCount;

        internal int EventSourceCreationCount => Volatile.Read(ref _eventSourceCreationCount);

        public IInputContextEventSource CreateEventSource() =>
            Interlocked.Increment(ref _eventSourceCreationCount) == 1
                ? new ThrowingEventSource()
                : new TestEventSource(_signal, _changeWasObserved);

        public RawInputContextObservation Observe() =>
            new(
                1,
                1,
                1,
                new TargetDescriptor(1, "target", "ControlType.Edit", "Edit", "Test"),
                InputState.English,
                InputStateEvidence.Unavailable,
                new InputEvidence(true, false, false, new ScreenRect(100, 120, 2, 20), null, null),
                UiAutomationCaretMethod.TextPattern,
                TextPattern2Status.PatternUnavailable,
                1);

        public RawInputContextObservation RefreshInputState(RawInputContextObservation current) =>
            current;

        public void Dispose()
        {
            _changeWasObserved.Dispose();
            _signal.Dispose();
        }
    }

    private sealed class ThrowingEventSource : IInputContextEventSource
    {
        public bool WaitForChange(TimeSpan fallbackInterval, CancellationToken cancellation) =>
            throw new InvalidOperationException("Injected event source failure.");

        public void Dispose()
        {
        }
    }
}
