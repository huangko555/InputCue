using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.App.Diagnostics;

internal sealed class ProbeDiagnosticRunner
{
    private const int TraceCaptureCapacity = 200;
    private static readonly TimeSpan TraceCaptureDuration = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions TraceJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    private readonly Dispatcher _dispatcher;
    private readonly Action<int> _shutdown;

    internal ProbeDiagnosticRunner(Dispatcher dispatcher, Action<int> shutdown)
    {
        _dispatcher = dispatcher;
        _shutdown = shutdown;
    }

    internal async Task RunSmokeTestAsync(string? traceOutputPath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var textBox = new TextBox
        {
            Text = "InputCue probe smoke test",
        };
        var readOnlyTextBox = new TextBox
        {
            IsReadOnly = true,
            Text = "Selectable read-only content",
        };
        var button = new Button
        {
            Content = "Non-editable target",
        };
        var panel = new StackPanel();
        panel.Children.Add(textBox);
        panel.Children.Add(readOnlyTextBox);
        panel.Children.Add(button);
        var window = new Window
        {
            Content = panel,
            Left = -32000,
            Top = -32000,
            Width = 240,
            Height = 100,
            ShowInTaskbar = false,
            ShowActivated = true,
            Title = "InputCue Probe Smoke Test",
            WindowStyle = WindowStyle.None,
        };

        try
        {
            window.Show();
            _ = window.Activate();
            _ = textBox.Focus();
            _ = Keyboard.Focus(textBox);
            await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            var engine = new InputContextEngine(
                sampleInterval: TimeSpan.FromMilliseconds(50),
                ignoreCurrentProcess: false);
            await using var observations = engine
                .WatchAsync(timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            var goldenObservations = new List<InputContextDiagnostic>();

            goldenObservations.Add(await WaitForAsync(observations, Eligibility.EditableCaret));

            textBox.Select(0, 5);
            goldenObservations.Add(await WaitForAsync(observations, Eligibility.EditableSelection));

            _ = readOnlyTextBox.Focus();
            _ = Keyboard.Focus(readOnlyTextBox);
            readOnlyTextBox.Select(0, 5);
            goldenObservations.Add(await WaitForAsync(observations, Eligibility.ReadOnlySelection));

            _ = button.Focus();
            _ = Keyboard.Focus(button);
            goldenObservations.Add(await WaitForAsync(observations, Eligibility.NoEditableFocus));

            if (traceOutputPath is not null)
            {
                await WriteGoldenTraceAsync(traceOutputPath, goldenObservations, timeout.Token);
            }

            _shutdown(0);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _shutdown(4);
        }
        catch (Exception)
        {
            _shutdown(5);
        }
        finally
        {
            window.Close();
        }
    }

    internal async Task RunTraceCaptureAsync(string tracePath)
    {
        using var timeout = new CancellationTokenSource(TraceCaptureDuration);
        var observations = new List<InputContextDiagnostic>();
        var engine = new InputContextEngine();

        try
        {
            await foreach (var diagnostic in engine.WatchAsync(timeout.Token))
            {
                observations.Add(diagnostic);
                if (observations.Count > TraceCaptureCapacity)
                {
                    observations.RemoveAt(0);
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }

        if (observations.Count == 0)
        {
            _shutdown(9);
            return;
        }

        try
        {
            await WriteGoldenTraceAsync(tracePath, observations, CancellationToken.None);
            _shutdown(0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _shutdown(5);
        }
    }

    internal async Task RunTsfProbeAsync(string outputPath)
    {
        var samples = await Task.Run(() =>
        {
            var results = new List<TsfProbeSample>();
            var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 15;
            while (Stopwatch.GetTimestamp() < deadline)
            {
                var result = TsfCaretProbe.Observe();
                results.Add(TsfProbeSample.From(result));
                Thread.Sleep(300);
            }

            return results;
        });

        try
        {
            var fullPath = Path.GetFullPath(outputPath);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var json = JsonSerializer.Serialize(
                new TsfProbeTrace(DateTimeOffset.UtcNow, samples),
                TraceJsonOptions);
            await File.WriteAllTextAsync(fullPath, json);
            _shutdown(0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _shutdown(5);
        }
    }

    private sealed record TsfProbeTrace(DateTimeOffset CapturedAt, IReadOnlyList<TsfProbeSample> Samples);

    private sealed record TsfProbeSample(
        uint ForegroundProcessId,
        string ForegroundProcessName,
        string Status,
        int HResult,
        double? X,
        double? Y,
        double? Width,
        double? Height,
        bool? Clipped,
        double DurationMilliseconds)
    {
        internal static TsfProbeSample From(TsfProbeResult result) => new(
            result.ForegroundProcessId,
            ProcessName(result.ForegroundProcessId),
            result.Status,
            result.HResult,
            result.TextExtent is { } rectangle ? rectangle.Left : null,
            result.TextExtent is { } rectangleY ? rectangleY.Top : null,
            result.TextExtent is { } rectangleWidth ? rectangleWidth.Right - rectangleWidth.Left : null,
            result.TextExtent is { } rectangleHeight ? rectangleHeight.Bottom - rectangleHeight.Top : null,
            result.Clipped,
            result.DurationMilliseconds);

        private static string ProcessName(uint processId)
        {
            if (processId == 0)
            {
                return string.Empty;
            }

            try
            {
                return Process.GetProcessById((int)processId).ProcessName ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }

    private static async Task<InputContextDiagnostic> WaitForAsync(
        IAsyncEnumerator<InputContextDiagnostic> observations,
        Eligibility expected)
    {
        while (await observations.MoveNextAsync())
        {
            var diagnostic = observations.Current;
            if (diagnostic.Target.ProcessId <= 0)
            {
                throw new InvalidOperationException("The probe did not identify the target process.");
            }

            if (diagnostic.Snapshot.Generation <= 0)
            {
                throw new InvalidOperationException("The probe did not advance its generation.");
            }

            if (diagnostic.Snapshot.Eligibility == expected)
            {
                return diagnostic;
            }
        }

        throw new InvalidOperationException($"The probe did not observe {expected}.");
    }

    private static async Task WriteGoldenTraceAsync(
        string tracePath,
        IEnumerable<InputContextDiagnostic> observations,
        CancellationToken cancellation)
    {
        var trace = InputContextGoldenTrace.Create(observations);
        var json = JsonSerializer.Serialize(trace, TraceJsonOptions);
        var fullTracePath = Path.GetFullPath(tracePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(fullTracePath)!);
        await File.WriteAllTextAsync(fullTracePath, json, cancellation);
    }
}
