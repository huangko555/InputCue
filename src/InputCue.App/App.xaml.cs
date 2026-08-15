using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InputCue.Core.InputContext;

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
            _ = RunProbeSmokeTestAsync();
            return;
        }

        new MainWindow().Show();
    }

    private async Task RunProbeSmokeTestAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var textBox = new TextBox
        {
            Text = "InputCue probe smoke test",
        };
        var window = new Window
        {
            Content = textBox,
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
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            var engine = new InputCue.Windows.InputContext.InputContextEngine(
                sampleInterval: TimeSpan.FromMilliseconds(50),
                ignoreCurrentProcess: false);
            await foreach (var diagnostic in engine.WatchAsync(timeout.Token))
            {
                if (diagnostic.Target.ProcessId <= 0)
                {
                    Shutdown(2);
                    return;
                }

                if (diagnostic.Snapshot.Generation <= 0)
                {
                    Shutdown(3);
                    return;
                }

                if (diagnostic.Snapshot.Eligibility is not Eligibility.EditableCaret)
                {
                    Shutdown(7);
                    return;
                }

                Shutdown(0);
                return;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            Shutdown(4);
            return;
        }
        catch (Exception)
        {
            Shutdown(5);
            return;
        }
        finally
        {
            window.Close();
        }

        Shutdown(6);
    }
}
