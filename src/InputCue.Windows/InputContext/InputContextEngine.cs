using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

public sealed class InputContextEngine : IDisposable
{
    private static readonly TimeSpan CircuitCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EventDebounceInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan MinimumEventObservationInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan RequestedObservationSettleInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan InputStateRefreshInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CaretTrackingInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PositionRetryInterval = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan ContextReturnWindow = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(2);
    private const int PositionRetryLimit = 3;
    private readonly bool _ignoreCurrentProcess;
    private readonly ObservationQueryRunner _queryRunner;
    private readonly IInputContextRuntime _runtime;
    private readonly TimeSpan _sampleInterval;
    private IInputContextEventSource? _activeEventSource;
    private bool _caretTrackingEnabled;
    private int _requestedObservationPending;
    private bool _disposed;

    internal int QueryWorkerCreationCount => _queryRunner.WorkerCreationCount;

    /// <summary>
    /// Enables faster full observations while an editable Caret indicator is visible.
    /// The existing single-worker timeout and circuit breaker continue to bound provider cost.
    /// </summary>
    public void SetCaretTrackingEnabled(bool enabled) =>
        Volatile.Write(ref _caretTrackingEnabled, enabled);

    /// <summary>
    /// Requests one full observation through the existing event-fed probe loop.
    /// The request does not enable continuous Caret tracking or add polling.
    /// </summary>
    public void RequestObservation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Exchange(ref _requestedObservationPending, 1);
        Volatile.Read(ref _activeEventSource)?.SignalChange();
    }

    public InputContextEngine(TimeSpan? sampleInterval = null)
        : this(
            sampleInterval,
            ignoreCurrentProcess: true,
            WindowsInputContextRuntime.Instance)
    {
    }

    internal InputContextEngine(TimeSpan? sampleInterval, bool ignoreCurrentProcess)
        : this(sampleInterval, ignoreCurrentProcess, WindowsInputContextRuntime.Instance)
    {
    }

    internal InputContextEngine(
        TimeSpan? sampleInterval,
        bool ignoreCurrentProcess,
        IInputContextRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _ignoreCurrentProcess = ignoreCurrentProcess;
        _runtime = runtime;
        _queryRunner = new ObservationQueryRunner(
            _runtime.Observe,
            QueryTimeout,
            CircuitCooldown);
        _sampleInterval = sampleInterval ?? DefaultSampleInterval;
        if (_sampleInterval < TimeSpan.FromMilliseconds(50) ||
            _sampleInterval > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleInterval),
                "Sample interval must be between 50 milliseconds and 5 seconds.");
        }
    }

    public async IAsyncEnumerable<InputContextDiagnostic> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var channel = Channel.CreateBounded<InputContextDiagnostic>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });
        using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var worker = new Thread(() => RunProbeLoop(channel.Writer, workerCancellation.Token))
        {
            IsBackground = true,
            Name = "InputCue.UIAutomation",
        };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();

        try
        {
            await foreach (var diagnostic in channel.Reader.ReadAllAsync(cancellation).ConfigureAwait(false))
            {
                yield return diagnostic;
            }
        }
        finally
        {
            workerCancellation.Cancel();
            _ = worker.Join(TimeSpan.FromSeconds(1));
        }
    }

    private void RunProbeLoop(
        ChannelWriter<InputContextDiagnostic> writer,
        CancellationToken cancellationToken)
    {
        var refreshTarget = RawInputContextObservation.Failure(ProbeIssue.SourceUnavailable, 0);
        var contextReturnTracker = new ContextReturnTracker(ContextReturnWindow);
        RawInputContextObservation? previousTarget = null;
        RawInputContextObservation? currentObservation = null;
        InputContextSnapshot? currentSnapshot = null;
        long generation = 0;
        long lastFullObservationAt = 0;
        var positionRetryCount = 0;
        var positionStabilizationCount = 0;
        var isPositionStabilizationObservation = false;
        var needsFullObservation = true;
        IInputContextEventSource? activeEventSource = null;

        try
        {
            using var eventSource = _runtime.CreateEventSource();
            activeEventSource = eventSource;
            Volatile.Write(ref _activeEventSource, eventSource);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (needsFullObservation)
                {
                    var previousSnapshotForStabilization = currentSnapshot;
                    var wasPositionStabilizationObservation = isPositionStabilizationObservation;
                    isPositionStabilizationObservation = false;
                    currentObservation = _queryRunner.Observe(cancellationToken);
                    lastFullObservationAt = Stopwatch.GetTimestamp();
                    needsFullObservation = false;

                    if (_ignoreCurrentProcess &&
                        currentObservation.Target.ProcessId == Environment.ProcessId)
                    {
                        contextReturnTracker.Reset();
                        previousTarget = currentObservation;
                        currentSnapshot = null;
                    }
                    else
                    {
                        var targetChanged = !InputTargetContinuity.IsSameTarget(
                            currentObservation,
                            previousTarget);
                        if (targetChanged)
                        {
                            generation = checked(generation + 1);
                            positionRetryCount = 0;
                            positionStabilizationCount = wasPositionStabilizationObservation
                                ? PositionRetryLimit
                                : 0;
                        }

                        previousTarget = currentObservation;

                        currentSnapshot = Classify(currentObservation, generation);
                        var suppressContextReplay = contextReturnTracker.Observe(
                            currentObservation,
                            currentSnapshot);
                        var deferContextLoss = contextReturnTracker.IsContextLossPending &&
                            !currentObservation.CaretOutsideVisibleBounds;
                        var isSameTargetStabilization =
                            wasPositionStabilizationObservation && !targetChanged;
                        if (!isSameTargetStabilization ||
                            HasStabilizationChange(previousSnapshotForStabilization, currentSnapshot))
                        {
                            _ = writer.TryWrite(CreateDiagnostic(
                                currentObservation,
                                currentSnapshot,
                                isSameTargetStabilization,
                                suppressContextReplay,
                                deferContextLoss));
                        }
                    }
                }

                var shouldTrackCaret = Volatile.Read(ref _caretTrackingEnabled) &&
                    currentSnapshot?.Eligibility is Eligibility.EditableCaret;
                var shouldTrackOffscreenCaret =
                    currentObservation?.CaretOutsideVisibleBounds is true &&
                    currentSnapshot?.Eligibility is Eligibility.PositionUnknown;
                var fullObservationInterval = (shouldTrackCaret || shouldTrackOffscreenCaret) &&
                    CaretTrackingInterval < _sampleInterval
                        ? CaretTrackingInterval
                        : _sampleInterval;
                var untilFullObservation = fullObservationInterval -
                    Stopwatch.GetElapsedTime(lastFullObservationAt);
                if (untilFullObservation <= TimeSpan.Zero)
                {
                    needsFullObservation = true;
                    continue;
                }

                var canRefreshInputState = currentSnapshot?.Eligibility is
                    Eligibility.EditableCaret or
                    Eligibility.EditableSelection or
                    Eligibility.PositionUnknown;
                var shouldRetryPosition = currentSnapshot?.Eligibility is Eligibility.PositionUnknown &&
                    positionRetryCount < PositionRetryLimit;
                var shouldStabilizePosition = currentSnapshot?.Eligibility is
                    Eligibility.EditableCaret or Eligibility.EditableSelection &&
                    positionStabilizationCount < PositionRetryLimit;
                var untilPositionRetry = PositionRetryInterval -
                    Stopwatch.GetElapsedTime(lastFullObservationAt);
                if ((shouldRetryPosition || shouldStabilizePosition) &&
                    untilPositionRetry <= TimeSpan.Zero)
                {
                    if (shouldRetryPosition)
                    {
                        positionRetryCount++;
                    }

                    if (shouldStabilizePosition)
                    {
                        positionStabilizationCount++;
                        isPositionStabilizationObservation = true;
                    }

                    needsFullObservation = true;
                    continue;
                }

                var waitInterval = canRefreshInputState &&
                    InputStateRefreshInterval < untilFullObservation
                        ? InputStateRefreshInterval
                        : untilFullObservation;
                if ((shouldRetryPosition || shouldStabilizePosition) &&
                    untilPositionRetry < waitInterval)
                {
                    waitInterval = untilPositionRetry;
                }
                var eventRaised = eventSource.WaitForChange(waitInterval, cancellationToken);
                if (eventRaised)
                {
                    var isRequestedObservation =
                        Interlocked.Exchange(ref _requestedObservationPending, 0) != 0;
                    if (isRequestedObservation)
                    {
                        WaitForRequestedObservationSettle(cancellationToken);
                    }
                    else
                    {
                        CoalesceChanges(eventSource, cancellationToken);
                        WaitForEventObservationBudget(lastFullObservationAt, cancellationToken);
                    }

                    positionRetryCount = 0;
                    needsFullObservation = true;
                    continue;
                }

                if (Stopwatch.GetElapsedTime(lastFullObservationAt) >= _sampleInterval)
                {
                    needsFullObservation = true;
                    continue;
                }

                if ((shouldRetryPosition || shouldStabilizePosition) &&
                    Stopwatch.GetElapsedTime(lastFullObservationAt) >= PositionRetryInterval)
                {
                    if (shouldRetryPosition)
                    {
                        positionRetryCount++;
                    }

                    if (shouldStabilizePosition)
                    {
                        positionStabilizationCount++;
                        isPositionStabilizationObservation = true;
                    }

                    needsFullObservation = true;
                    continue;
                }

                if (!canRefreshInputState || currentObservation is null)
                {
                    continue;
                }

                refreshTarget = currentObservation;
                cancellationToken.ThrowIfCancellationRequested();
                var refreshed = _runtime.RefreshInputState(refreshTarget);
                if (eventSource.WaitForChange(TimeSpan.Zero, cancellationToken))
                {
                    CoalesceChanges(eventSource, cancellationToken);
                    needsFullObservation = true;
                    continue;
                }

                if (refreshed.Fingerprint != currentObservation.Fingerprint)
                {
                    needsFullObservation = true;
                    continue;
                }

                var stateChanged = refreshed.InputState != currentObservation.InputState ||
                    refreshed.InputStateEvidence != currentObservation.InputStateEvidence;
                currentObservation = refreshed;
                if (stateChanged)
                {
                    currentSnapshot = Classify(currentObservation, generation);
                    _ = writer.TryWrite(CreateDiagnostic(currentObservation, currentSnapshot));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            writer.TryComplete(exception);
        }
        finally
        {
            if (activeEventSource is not null)
            {
                _ = Interlocked.CompareExchange(
                    ref _activeEventSource,
                    null,
                    activeEventSource);
            }

            writer.TryComplete();
        }
    }

    private static InputContextSnapshot Classify(
        RawInputContextObservation observation,
        long generation) =>
        InputContextClassifier.Classify(
            generation,
            DateTimeOffset.UtcNow,
            observation.InputState,
            observation.Evidence);

    private static bool HasStabilizationChange(
        InputContextSnapshot? previous,
        InputContextSnapshot current) =>
        previous is null ||
        previous.Anchor != current.Anchor ||
        previous.Eligibility != current.Eligibility ||
        previous.InputState != current.InputState;

    private static InputContextDiagnostic CreateDiagnostic(
        RawInputContextObservation observation,
        InputContextSnapshot snapshot,
        bool isPositionStabilization = false,
        bool suppressContextReplay = false,
        bool deferContextLoss = false) =>
        new(
            snapshot,
            observation.Target,
            observation.Evidence.HasEditableFocus,
            observation.Evidence.IsReadOnly,
            observation.Evidence.HasSelection,
            observation.Evidence.UiAutomationCaret,
            observation.UiAutomationCaretMethod,
            observation.TextPattern2Status,
            observation.Evidence.Win32Caret,
            observation.Evidence.MsaaCaret,
            observation.Evidence.Issue,
            observation.DurationMilliseconds,
            observation.InputStateEvidence,
            isPositionStabilization,
            suppressContextReplay,
            deferContextLoss);

    private static void CoalesceChanges(
        IInputContextEventSource eventSource,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var remaining = EventDebounceInterval;
        while (eventSource.WaitForChange(remaining, cancellationToken))
        {
            remaining = EventDebounceInterval - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queryRunner.Dispose();
    }

    private static void WaitForEventObservationBudget(
        long lastFullObservationAt,
        CancellationToken cancellationToken)
    {
        var remaining = MinimumEventObservationInterval -
            Stopwatch.GetElapsedTime(lastFullObservationAt);
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        if (cancellationToken.WaitHandle.WaitOne(remaining))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static void WaitForRequestedObservationSettle(CancellationToken cancellationToken)
    {
        if (cancellationToken.WaitHandle.WaitOne(RequestedObservationSettleInterval))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

}
