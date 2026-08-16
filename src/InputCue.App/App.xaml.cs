using System.Windows;
using InputCue.App.Diagnostics;
using InputCue.Windows.SingleInstance;

namespace InputCue.App;

public partial class App : Application
{
    private const string UiInstanceName = "InputCue.UI.v1";
    private SingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--probe-smoke-test", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var runner = new ProbeDiagnosticRunner(Dispatcher, Shutdown);
            _ = runner.RunSmokeTestAsync(ReadOption(e.Args, "--trace-output"));
            return;
        }

        if (e.Args.Contains("--capture-trace", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var tracePath = ReadOption(e.Args, "--capture-trace");
            if (tracePath is null)
            {
                Shutdown(8);
                return;
            }

            var runner = new ProbeDiagnosticRunner(Dispatcher, Shutdown);
            _ = runner.RunTraceCaptureAsync(tracePath);
            return;
        }

        if (e.Args.Contains("--probe-tsf", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var outputPath = ReadOption(e.Args, "--probe-tsf");
            if (outputPath is null)
            {
                Shutdown(8);
                return;
            }

            var runner = new ProbeDiagnosticRunner(Dispatcher, Shutdown);
            _ = runner.RunTsfProbeAsync(outputPath);
            return;
        }

        _singleInstance = SingleInstanceCoordinator.TryAcquire(
            UiInstanceName,
            RequestMainWindowActivation);
        if (_singleInstance is null)
        {
            Shutdown(0);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private void RequestMainWindowActivation()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is not { } window)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            _ = window.Activate();
        });
    }

    private static string? ReadOption(string[] arguments, string option)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], option, StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
