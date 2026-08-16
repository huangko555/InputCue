using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using InputCue.Core.Indicator;
using InputCue.Core.InputContext;
using InputCue.Overlay;
using InputCue.Windows.InputContext;
using Microsoft.Win32;

namespace InputCue.App;

public partial class MainWindow : Window, IDisposable
{
    private const int HistoryCapacity = 200;
    private static readonly TimeSpan AnimationTickInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan CapsLockPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    private readonly InputContextEngine _engine = new();
    private IndicatorSession _indicatorSession;
    private readonly IndicatorOverlayPresenter _overlayPresenter = new();
    private readonly InputContextTraceBuffer _history = new(HistoryCapacity);
    private readonly DispatcherTimer _indicatorTimer;
    private readonly RawKeyboardInputMonitor _keyboardInputMonitor = new();
    private InputContextDiagnostic? _lastBaseDiagnostic;
    private CancellationTokenSource? _watchCancellation;
    private bool _capsLockEnabled;
    private bool _disposed;
    private bool _indicatorEnabled = true;

    public event EventHandler? WatchingStateChanged;

    public bool IsWatching => _watchCancellation is not null;

    public MainWindow()
    {
        InitializeComponent();
        _indicatorSession = CreateIndicatorSession(
            displayDurationMilliseconds: 1000,
            minimumDisplayDurationMilliseconds: 300);
        _capsLockEnabled = UiCapsLockProbe.IsEnabled();
        _indicatorTimer = new DispatcherTimer(
            CapsLockPollInterval,
            DispatcherPriority.Background,
            OnIndicatorTick,
            Dispatcher);
        _keyboardInputMonitor.EditingKeyPressed += OnEditingKeyPressed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = _keyboardInputMonitor.Attach(this);
        StartWatching();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watchCancellation?.Cancel();
        _watchCancellation?.Dispose();
        _watchCancellation = null;
        _indicatorTimer.Stop();
        _keyboardInputMonitor.EditingKeyPressed -= OnEditingKeyPressed;
        _keyboardInputMonitor.Dispose();
        _overlayPresenter.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        ToggleWatching();
    }

    public void ToggleWatching()
    {
        if (_watchCancellation is null)
        {
            StartWatching();
            return;
        }

        StopWatching();
    }

    private void OnIndicatorEnabledChanged(object sender, RoutedEventArgs e)
    {
        _indicatorEnabled = IndicatorEnabledCheckBox.IsChecked is true;
        if (!_indicatorEnabled)
        {
            _overlayPresenter.Hide();
            _indicatorTimer.Stop();
            return;
        }

        if (_lastBaseDiagnostic is { } diagnostic)
        {
            var indicatorState = _indicatorSession.Advance(DateTimeOffset.UtcNow);
            _overlayPresenter.Update(indicatorState);
            UpdateIndicatorTimer(indicatorState, diagnostic.Snapshot.Eligibility);
        }
    }

    private void OnTimingSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadMilliseconds(DisplayDurationTextBox.Text, out var displayDuration) ||
            !TryReadMilliseconds(MinimumDisplayDurationTextBox.Text, out var minimumDisplayDuration))
        {
            StatusText.Text = "显示时长必须是 0 到 60000 之间的毫秒数。";
            return;
        }

        _indicatorSession = CreateIndicatorSession(displayDuration, minimumDisplayDuration);
        _overlayPresenter.Hide();
        _indicatorTimer.Stop();
        StatusText.Text = $"已应用显示时长：{displayDuration} ms；输入后最短：{minimumDisplayDuration} ms。";
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_history.Observations.Count == 0)
        {
            StatusText.Text = "没有可导出的外部应用观察。";
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".json",
            FileName = $"InputCue-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Filter = "JSON 文件 (*.json)|*.json",
            Title = "导出 InputCue 脱敏诊断",
        };

        if (dialog.ShowDialog(this) is not true)
        {
            return;
        }

        try
        {
            var trace = InputContextTrace.Create(_history.Observations);
            var json = JsonSerializer.Serialize(trace, JsonOptions);
            await File.WriteAllTextAsync(dialog.FileName, json, Encoding.UTF8);
            StatusText.Text = $"已导出 {_history.Observations.Count} 条脱敏观察。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "导出失败：目标文件不可写。";
        }
    }

    private void StartWatching()
    {
        if (_watchCancellation is not null)
        {
            return;
        }

        _watchCancellation = new CancellationTokenSource();
        PauseButton.Content = "暂停";
        StatusText.Text = "正在监听。请切换到其他应用测试，InputCue 自身不会成为观察目标。";
        WatchingStateChanged?.Invoke(this, EventArgs.Empty);
        _ = WatchAsync(_watchCancellation.Token);
    }

    private void StopWatching()
    {
        var cancellation = _watchCancellation;
        _watchCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        _overlayPresenter.Hide();
        _indicatorTimer.Stop();
        PauseButton.Content = "继续";
        StatusText.Text = "诊断探针已暂停。";
        WatchingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var diagnostic in _engine.WatchAsync(cancellationToken))
            {
                await Dispatcher.InvokeAsync(() => ShowDiagnostic(diagnostic));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = "诊断探针意外停止。未记录异常正文，以免包含敏感数据。";
                PauseButton.Content = "继续";
                _watchCancellation?.Dispose();
                _watchCancellation = null;
                WatchingStateChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private void ShowDiagnostic(InputContextDiagnostic diagnostic)
    {
        _lastBaseDiagnostic = diagnostic;
        _capsLockEnabled = UiCapsLockProbe.IsEnabled();
        PresentDiagnostic(ApplyCapsLock(diagnostic, _capsLockEnabled));
    }

    private void PresentDiagnostic(InputContextDiagnostic diagnostic)
    {
        _history.Add(diagnostic);
        var indicatorState = _indicatorSession.Observe(
            diagnostic.Snapshot,
            DateTimeOffset.UtcNow);
        if (_indicatorEnabled)
        {
            _overlayPresenter.Update(indicatorState);
        }
        else
        {
            _overlayPresenter.Hide();
        }

        UpdateIndicatorTimer(indicatorState, diagnostic.Snapshot.Eligibility);

        StatusText.Text =
            $"正在监听 · 最近观察 {diagnostic.Snapshot.ObservedAt.ToLocalTime():HH:mm:ss.fff} · " +
            $"耗时 {diagnostic.DurationMilliseconds:F1} ms";
        DiagnosticText.Text = FormatDiagnostic(diagnostic);
    }

    private void OnIndicatorTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var capsLockEnabled = UiCapsLockProbe.IsEnabled();
        if (capsLockEnabled != _capsLockEnabled)
        {
            _capsLockEnabled = capsLockEnabled;
            if (!IsActive && _lastBaseDiagnostic is { } diagnostic)
            {
                PresentDiagnostic(ApplyCapsLock(diagnostic, capsLockEnabled, now));
                return;
            }
        }

        var indicatorState = _indicatorSession.Advance(now);
        if (_indicatorEnabled)
        {
            _overlayPresenter.Update(indicatorState);
        }

        UpdateIndicatorTimer(
            indicatorState,
            _lastBaseDiagnostic?.Snapshot.Eligibility ?? Eligibility.Unknown);
    }

    private void OnEditingKeyPressed(object? sender, EventArgs e)
    {
        var eligibility = _lastBaseDiagnostic?.Snapshot.Eligibility ?? Eligibility.Unknown;
        if (!_indicatorEnabled ||
            IsActive ||
            eligibility is not (Eligibility.EditableCaret or Eligibility.EditableSelection))
        {
            return;
        }

        var wasVisible = _indicatorSession.Current.IsVisible;
        var indicatorState = _indicatorSession.ObserveInputActivity(DateTimeOffset.UtcNow);
        if (!wasVisible)
        {
            return;
        }

        _overlayPresenter.Update(indicatorState);
        UpdateIndicatorTimer(indicatorState, eligibility);
    }

    private void UpdateIndicatorTimer(IndicatorViewState indicatorState, Eligibility eligibility)
    {
        if (!_indicatorEnabled || _watchCancellation is null)
        {
            _indicatorTimer.Stop();
            return;
        }

        TimeSpan? interval = indicatorState.IsVisible
            ? AnimationTickInterval
            : eligibility is Eligibility.EditableCaret or Eligibility.EditableSelection
                ? CapsLockPollInterval
                : null;
        if (interval is null)
        {
            _indicatorTimer.Stop();
            return;
        }

        if (_indicatorTimer.Interval != interval.Value)
        {
            _indicatorTimer.Interval = interval.Value;
        }

        if (!_indicatorTimer.IsEnabled)
        {
            _indicatorTimer.Start();
        }
    }

    private static InputContextDiagnostic ApplyCapsLock(
        InputContextDiagnostic diagnostic,
        bool capsLockEnabled,
        DateTimeOffset? observedAt = null) =>
        diagnostic with
        {
            Snapshot = diagnostic.Snapshot with
            {
                ObservedAt = observedAt ?? diagnostic.Snapshot.ObservedAt,
                InputState = EffectiveInputState.Resolve(
                    diagnostic.Snapshot.InputState,
                    capsLockEnabled),
            },
        };

    private static IndicatorSession CreateIndicatorSession(
        int displayDurationMilliseconds,
        int minimumDisplayDurationMilliseconds) =>
        new(new IndicatorSessionOptions(
            TimeSpan.FromMilliseconds(displayDurationMilliseconds),
            TimeSpan.FromMilliseconds(150))
        {
            MinimumDisplayDuration = TimeSpan.FromMilliseconds(minimumDisplayDurationMilliseconds),
        });

    private static bool TryReadMilliseconds(string text, out int milliseconds) =>
        int.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out milliseconds) &&
        milliseconds is >= 0 and <= 60000;

    private static string FormatDiagnostic(InputContextDiagnostic diagnostic)
    {
        var snapshot = diagnostic.Snapshot;
        var inputStateEvidence = diagnostic.InputStateEvidence ?? InputStateEvidence.Unavailable;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Generation        {snapshot.Generation}
            Eligibility       {snapshot.Eligibility}
            InputState        {snapshot.InputState}
            InputLanguage     {FormatHex(inputStateEvidence.LanguageId)}
            IsIME             {ValueOrUnknown(inputStateEvidence.IsIme)}
            HasIMEContext     {ValueOrUnknown(inputStateEvidence.HasImeContext)}
            IMEOpen           {ValueOrUnknown(inputStateEvidence.ImeOpen)}
            ConversionMode    {FormatHex(inputStateEvidence.ConversionMode)}
            HasDefaultIMEWnd  {ValueOrUnknown(inputStateEvidence.HasDefaultImeWindow)}
            WindowOpenStatus  {FormatHex(inputStateEvidence.ImeWindowOpenStatus)}
            WindowConvMode    {FormatHex(inputStateEvidence.ImeWindowConversionMode)}
            EvidenceGrade     {snapshot.EvidenceGrade}
            ReasonCode        {snapshot.ReasonCode}

            Process           {diagnostic.Target.ProcessName} ({diagnostic.Target.ProcessId})
            Framework         {ValueOrUnknown(diagnostic.Target.FrameworkId)}
            ControlType       {ValueOrUnknown(diagnostic.Target.ControlType)}
            ClassName         {ValueOrUnknown(diagnostic.Target.ClassName)}

            HasEditableFocus  {diagnostic.HasEditableFocus}
            IsReadOnly        {ValueOrUnknown(diagnostic.IsReadOnly)}
            HasSelection      {ValueOrUnknown(diagnostic.HasSelection)}
            AnchorSource      {snapshot.AnchorSource}
            Anchor            {FormatRectangle(snapshot.Anchor)}

            UIA Caret         {FormatRectangle(diagnostic.UiAutomationCaret)}
            UIA Caret Method  {diagnostic.UiAutomationCaretMethod}
            TextPattern2      {diagnostic.TextPattern2Status}
            Win32 Caret       {FormatRectangle(diagnostic.Win32Caret)}
            MSAA Caret        {FormatRectangle(diagnostic.MsaaCaret)}
            ProbeIssue        {diagnostic.Issue}
            Duration          {diagnostic.DurationMilliseconds:F1} ms
            """);
    }

    private static string FormatRectangle(ScreenRect? rectangle) => rectangle is null
        ? "unknown"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"x={rectangle.Value.X:F1}, y={rectangle.Value.Y:F1}, " +
            $"w={rectangle.Value.Width:F1}, h={rectangle.Value.Height:F1}");

    private static string ValueOrUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static string ValueOrUnknown(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "unknown",
    };

    private static string FormatHex(ushort? value) => value is null
        ? "unknown"
        : string.Create(CultureInfo.InvariantCulture, $"0x{value.Value:X4}");

    private static string FormatHex(uint? value) => value is null
        ? "unknown"
        : string.Create(CultureInfo.InvariantCulture, $"0x{value.Value:X8}");
}
