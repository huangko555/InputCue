using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using InputCue.App.Diagnostics;
using InputCue.Core.Settings;
using InputCue.Windows.SingleInstance;
using InputCue.Windows.Startup;
using Forms = System.Windows.Forms;

namespace InputCue.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Application lifetime; OnExit releases all tray resources.")]
public partial class App : System.Windows.Application
{
    private const string UiInstanceName = "InputCue.UI.v1";
    private SingleInstanceCoordinator? _singleInstance;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _pauseMenuItem;
    private bool _isExiting;

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

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InputCue",
            "settings.json");
        var settingsStore = new InputCueSettingsStore(settingsPath);
        var executablePath = Environment.ProcessPath;
        var mainWindow = new MainWindow(
            settingsStore.Load(),
            settingsStore.TrySave,
            StartupRegistration.IsEnabled(),
            enabled => executablePath is not null &&
                StartupRegistration.TrySetEnabled(enabled, executablePath));
        MainWindow = mainWindow;
        mainWindow.Closing += OnMainWindowClosing;
        mainWindow.WatchingStateChanged += OnWatchingStateChanged;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        InitializeTrayIcon(mainWindow);
        mainWindow.Start();
        if (!e.Args.Contains("--background", StringComparer.Ordinal))
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            window.WatchingStateChanged -= OnWatchingStateChanged;
            window.Closing -= OnMainWindowClosing;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;
        _pauseMenuItem = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private void RequestMainWindowActivation()
    {
        _ = Dispatcher.BeginInvoke(OpenMainWindow);
    }

    private void InitializeTrayIcon(MainWindow window)
    {
        var openItem = new Forms.ToolStripMenuItem("打开设置", null, (_, _) => OpenMainWindow());
        _pauseMenuItem = new Forms.ToolStripMenuItem();
        _pauseMenuItem.Click += (_, _) => window.ToggleWatching();
        var exitItem = new Forms.ToolStripMenuItem("退出", null, (_, _) => ExitApplication());

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(_pauseMenuItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => OpenMainWindow();
        UpdateTrayState(window);
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting || sender is not MainWindow window)
        {
            return;
        }

        e.Cancel = true;
        window.Hide();
    }

    private void OnWatchingStateChanged(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            UpdateTrayState(window);
        }
    }

    private void UpdateTrayState(MainWindow window)
    {
        if (_pauseMenuItem is null || _trayIcon is null)
        {
            return;
        }

        _pauseMenuItem.Text = window.IsWatching ? "暂停提示" : "继续提示";
        _trayIcon.Text = !window.IsWatching
            ? "InputCue - 已暂停"
            : window.IsRecovering
                ? "InputCue - 正在恢复"
                : "InputCue - 正在运行";
    }

    private void OpenMainWindow()
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
    }

    private void ExitApplication()
    {
        _isExiting = true;
        MainWindow?.Close();
        Shutdown(0);
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
