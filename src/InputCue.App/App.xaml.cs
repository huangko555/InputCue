using System.Windows;
using InputCue.App.Diagnostics;

namespace InputCue.App;

public partial class App : Application
{
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

        new MainWindow().Show();
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
