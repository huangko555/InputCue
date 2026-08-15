using System.Runtime.CompilerServices;
using System.Threading.Channels;
using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

public sealed class InputContextEngine
{
    private static readonly TimeSpan CircuitCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EventDebounceInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(1);
    private readonly bool _ignoreCurrentProcess;
    private readonly TimeSpan _sampleInterval;

    public InputContextEngine(TimeSpan? sampleInterval = null)
        : this(sampleInterval, ignoreCurrentProcess: true)
    {
    }

    internal InputContextEngine(TimeSpan? sampleInterval, bool ignoreCurrentProcess)
    {
        _ignoreCurrentProcess = ignoreCurrentProcess;
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
        using var eventSource = WindowsInputContextEventSource.Create();
        var queryRunner = new ObservationQueryRunner(
            ObserveOnce,
            QueryTimeout,
            CircuitCooldown);
        ObservationFingerprint? previousFingerprint = null;
        long generation = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var observation = queryRunner.Observe(cancellationToken);
                if (_ignoreCurrentProcess && observation.Target.ProcessId == Environment.ProcessId)
                {
                    previousFingerprint = observation.Fingerprint;
                }
                else
                {
                    if (observation.Fingerprint != previousFingerprint)
                    {
                        generation = checked(generation + 1);
                        previousFingerprint = observation.Fingerprint;
                    }

                    var snapshot = InputContextClassifier.Classify(
                        generation,
                        DateTimeOffset.UtcNow,
                        InputState.Unknown,
                        observation.Evidence);
                    var diagnostic = new InputContextDiagnostic(
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
                        observation.DurationMilliseconds);
                    _ = writer.TryWrite(diagnostic);
                }

                var eventRaised = eventSource.WaitForChange(_sampleInterval, cancellationToken);
                if (eventRaised && cancellationToken.WaitHandle.WaitOne(EventDebounceInterval))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static RawInputContextObservation ObserveOnce()
    {
        using var probe = new WindowsInputContextProbe();
        return probe.Observe();
    }
}
