using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Windows;
using InputCue.App.Diagnostics;
using InputCue.Core.Settings;
using InputCue.Update;
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
    private const string GitHubUrl = "https://github.com/huangko555/InputCue";
    private static readonly Uri UpdateManifestUri = new(
        GitHubUrl + "/releases/latest/download/portable-releases.json");
    private static readonly Uri UpdateSignatureUri = new(
        GitHubUrl + "/releases/latest/download/portable-releases.json.sig");
    private SingleInstanceCoordinator? _singleInstance;
    private PortableUpdateManager? _updateManager;
    private CancellationTokenSource? _updateCancellation;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _applicationIcon;
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

        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var isPortable = File.Exists(Path.Combine(
            baseDirectory,
            PortableUpdateManager.PortableMarkerFileName));
        var dataDirectory = isPortable
            ? Path.Combine(baseDirectory, "data")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InputCue");
        var settingsPath = Path.Combine(dataDirectory, "settings.json");
        var settingsStore = new InputCueSettingsStore(settingsPath);
        var executablePath = Environment.ProcessPath;
        _updateCancellation = new CancellationTokenSource();
        _updateManager = new PortableUpdateManager(
            baseDirectory,
            dataDirectory,
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0),
            UpdateManifestUri,
            UpdateSignatureUri);
        var mainWindow = new MainWindow(
            settingsStore.Load(),
            settingsStore.TrySave,
            StartupRegistration.IsEnabled(),
            enabled => executablePath is not null &&
                StartupRegistration.TrySetEnabled(enabled, executablePath),
            () => CheckAndApplyUpdateAsync(manual: true),
            OpenGitHub);
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

        _ = RunAutomaticUpdateScheduleAsync(_updateCancellation.Token);
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

        _applicationIcon?.Dispose();
        _applicationIcon = null;

        _trayMenu?.Dispose();
        _trayMenu = null;
        _pauseMenuItem = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _updateCancellation = null;
        _updateManager?.Dispose();
        _updateManager = null;
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

        _applicationIcon = Environment.ProcessPath is { } processPath
            ? System.Drawing.Icon.ExtractAssociatedIcon(processPath)
            : null;
        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = _applicationIcon ?? System.Drawing.SystemIcons.Application,
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
                : window.IsFullScreenAutoPaused
                    ? "InputCue - 全屏暂停"
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

    private async Task RunAutomaticUpdateScheduleAsync(CancellationToken cancellationToken)
    {
        if (_updateManager is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await CheckAndApplyUpdateAsync(manual: false).ConfigureAwait(true);
                if (result.Status == PortableUpdateStatus.Unsupported || _isExiting)
                {
                    return;
                }

                var delay = _updateManager.GetAutomaticCheckDelay(DateTimeOffset.UtcNow);
                if (delay <= TimeSpan.Zero)
                {
                    delay = PortableUpdateManager.AutomaticCheckInterval;
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<PortableUpdateResult> CheckAndApplyUpdateAsync(bool manual)
    {
        if (_updateManager is null || _updateCancellation is null)
        {
            return new PortableUpdateResult(
                PortableUpdateStatus.Unsupported,
                "自动更新尚未初始化。");
        }

        var result = await _updateManager.CheckAsync(manual, _updateCancellation.Token)
            .ConfigureAwait(true);
        if (result.Status != PortableUpdateStatus.UpdateReady)
        {
            return result;
        }

        var executablePath = Environment.ProcessPath;
        if (executablePath is null ||
            !_updateManager.TryLaunchUpdater(result, Environment.ProcessId, executablePath))
        {
            return result with
            {
                Status = PortableUpdateStatus.Failed,
                Message = "新版已下载，但更新器无法启动。",
            };
        }

        if (manual && MainWindow is MainWindow mainWindow)
        {
            var notice = PortableUpdateNotice.FromResult(result);
            mainWindow.ShowUpdateNotice(notice);
            await Task.Delay(notice.Duration).ConfigureAwait(true);
        }

        _isExiting = true;
        _ = Dispatcher.BeginInvoke(() => Shutdown(0));
        return result;
    }

    private static void OpenGitHub()
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
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
