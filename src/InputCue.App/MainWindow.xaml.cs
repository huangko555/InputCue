using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InputCue.Core.Indicator;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;
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
    private readonly Func<InputCueSettings, bool> _saveSettings;
    private readonly Func<bool, bool> _setStartWithWindows;
    private InputContextDiagnostic? _lastBaseDiagnostic;
    private CancellationTokenSource? _watchCancellation;
    private bool _capsLockEnabled;
    private bool _disposed;
    private bool _indicatorEnabled;
    private bool _settingsInitialized;
    private bool _startupSettingInitialized;
    private bool _started;
    private int _displayDurationMilliseconds;
    private int _minimumDisplayDurationMilliseconds;
    private IndicatorStyle _style;
    private IndicatorStyle _editingStyle;
    private IndicatorAppearanceSettings _dotAppearance;
    private IndicatorAppearanceSettings _lightBadgeAppearance;
    private IndicatorAppearanceSettings _shadowBadgeAppearance;
    private long _diagnosticGenerationBaseline;
    private bool _previewAllowed;
    private string _chineseDotColor;
    private string _englishDotColor;
    private string _englishUsDotColor;
    private string _capsLockDotColor;

    public event EventHandler? WatchingStateChanged;

    public bool IsWatching => _watchCancellation is not null;

    public MainWindow(
        InputCueSettings settings,
        Func<InputCueSettings, bool> saveSettings,
        bool startWithWindows,
        Func<bool, bool> setStartWithWindows)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(saveSettings);
        ArgumentNullException.ThrowIfNull(setStartWithWindows);
        _saveSettings = saveSettings;
        _setStartWithWindows = setStartWithWindows;
        InitializeComponent();
        _indicatorEnabled = settings.IndicatorEnabled;
        _displayDurationMilliseconds = settings.DisplayDurationMilliseconds;
        _minimumDisplayDurationMilliseconds = settings.MinimumDisplayDurationMilliseconds;
        _style = settings.Style;
        _editingStyle = _style;
        _dotAppearance = settings.GetAppearance(IndicatorStyle.Dot);
        _lightBadgeAppearance = settings.GetAppearance(IndicatorStyle.LightBadge);
        _shadowBadgeAppearance = settings.GetAppearance(IndicatorStyle.ShadowBadge);
        _chineseDotColor = settings.ChineseDotColor;
        _englishDotColor = settings.EnglishDotColor;
        _englishUsDotColor = settings.EnglishUsDotColor;
        _capsLockDotColor = settings.CapsLockDotColor;
        DisplayDurationTextBox.Text = _displayDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        MinimumDisplayDurationTextBox.Text =
            _minimumDisplayDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        ChineseDotColorTextBox.Text = _chineseDotColor;
        EnglishDotColorTextBox.Text = _englishDotColor;
        EnglishUsDotColorTextBox.Text = _englishUsDotColor;
        CapsLockDotColorTextBox.Text = _capsLockDotColor;
        SetStyleSelection(_style);
        LoadAppearance(_editingStyle);
        UpdateStylePanels();
        ConfigureOverlay();
        UpdatePlacementPreview();
        _indicatorSession = CreateIndicatorSession(
            _displayDurationMilliseconds,
            _minimumDisplayDurationMilliseconds);
        _capsLockEnabled = UiCapsLockProbe.IsEnabled();
        _indicatorTimer = new DispatcherTimer(
            CapsLockPollInterval,
            DispatcherPriority.Background,
            OnIndicatorTick,
            Dispatcher);
        _keyboardInputMonitor.EditingKeyPressed += OnEditingKeyPressed;
        IndicatorEnabledCheckBox.IsChecked = _indicatorEnabled;
        StartWithWindowsCheckBox.IsChecked = startWithWindows;
        _settingsInitialized = true;
        _startupSettingInitialized = true;
    }

    internal void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
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
        _engine.Dispose();
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
        var saved = PersistSettings();
        if (!_indicatorEnabled)
        {
            _overlayPresenter.Hide();
            _indicatorTimer.Stop();
            ShowPreviewOverlay();
            if (!saved)
            {
                StatusText.Text = "提示已关闭，但设置未能保存。";
            }

            return;
        }

        if (IsActive)
        {
            ShowPreviewOverlay();
        }
        else if (_lastBaseDiagnostic is { } diagnostic)
        {
            var indicatorState = _indicatorSession.Advance(DateTimeOffset.UtcNow);
            _overlayPresenter.Update(indicatorState);
            UpdateIndicatorTimer(indicatorState, diagnostic.Snapshot.Eligibility);
        }

        if (!saved)
        {
            StatusText.Text = "提示已启用，但设置未能保存。";
        }
    }

    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (!_startupSettingInitialized)
        {
            return;
        }

        var requested = StartWithWindowsCheckBox.IsChecked is true;
        if (_setStartWithWindows(requested))
        {
            StatusText.Text = requested ? "已启用开机自动启动。" : "已关闭开机自动启动。";
            return;
        }

        _startupSettingInitialized = false;
        StartWithWindowsCheckBox.IsChecked = !requested;
        _startupSettingInitialized = true;
        StatusText.Text = "开机启动设置未能保存。";
    }

    private void OnTimingSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadMilliseconds(DisplayDurationTextBox.Text, out var displayDuration) ||
            !TryReadMilliseconds(MinimumDisplayDurationTextBox.Text, out var minimumDisplayDuration))
        {
            StatusText.Text = "显示时长必须是 0 到 60000 之间的毫秒数。";
            return;
        }

        var style = GetSelectedStyle();
        if (!TryReadAppearance(style, out var appearance))
        {
            StatusText.Text =
                $"圆点尺寸必须为 {InputCueSettings.MinimumIndicatorSizeDip} 到 " +
                $"{InputCueSettings.MaximumIndicatorSizeDip}；徽标尺寸必须为 " +
                $"{InputCueSettings.MinimumLightBadgeSizeDip} 到 " +
                $"{InputCueSettings.MaximumLightBadgeSizeDip}；横向和纵向微调必须为 " +
                $"{InputCueSettings.MinimumOffsetDip} 到 {InputCueSettings.MaximumOffsetDip}。";
            return;
        }

        if (!TryReadDotColors(
                out var chineseDotColor,
                out var englishDotColor,
                out var englishUsDotColor,
                out var capsLockDotColor))
        {
            StatusText.Text = "圆点颜色必须是 6 位十六进制颜色值。";
            return;
        }

        _displayDurationMilliseconds = displayDuration;
        _minimumDisplayDurationMilliseconds = minimumDisplayDuration;
        _style = style;
        _editingStyle = style;
        SetAppearance(style, appearance);
        _chineseDotColor = chineseDotColor;
        _englishDotColor = englishDotColor;
        _englishUsDotColor = englishUsDotColor;
        _capsLockDotColor = capsLockDotColor;
        _indicatorSession = CreateIndicatorSession(displayDuration, minimumDisplayDuration);
        _overlayPresenter.Hide();
        ConfigureOverlay();
        _indicatorTimer.Stop();
        StatusText.Text = PersistSettings()
            ? $"已应用并保存：{StyleName(style)}，{PlacementName(appearance.Placement)}，尺寸 {appearance.SizeDip}，" +
              $"微调 ({appearance.HorizontalOffsetDip}, {appearance.VerticalOffsetDip})。"
            : "设置已应用，但未能保存。";
    }

    private void OnStylePreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsInitialized)
        {
            if (TryReadAppearance(_editingStyle, out var appearance))
            {
                SetAppearance(_editingStyle, appearance);
            }

            _editingStyle = GetSelectedStyle();
            LoadAppearance(_editingStyle);
            UpdateStylePanels();
            UpdatePlacementPreview();
        }
    }

    private void OnPlacementPreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsInitialized)
        {
            UpdatePlacementPreview();
        }
    }

    private void OnResetPlacementClick(object sender, RoutedEventArgs e)
    {
        var style = GetSelectedStyle();
        SetPlacementSelection(InputCueSettings.DefaultPlacement);
        var sizeText = (style == IndicatorStyle.Dot
                ? InputCueSettings.DefaultIndicatorSizeDip
                : InputCueSettings.DefaultLightBadgeSizeDip)
            .ToString(CultureInfo.InvariantCulture);
        if (style == IndicatorStyle.Dot)
        {
            DotSizeTextBox.Text = sizeText;
        }
        else
        {
            BadgeSizeTextBox.Text = sizeText;
        }

        HorizontalOffsetTextBox.Text = "0";
        VerticalOffsetTextBox.Text = "0";
        ChineseDotColorTextBox.Text = InputCueSettings.DefaultChineseDotColor;
        EnglishDotColorTextBox.Text = InputCueSettings.DefaultEnglishDotColor;
        EnglishUsDotColorTextBox.Text = InputCueSettings.DefaultEnglishUsDotColor;
        CapsLockDotColorTextBox.Text = InputCueSettings.DefaultCapsLockDotColor;
        UpdatePlacementPreview();
    }

    private void OnHexColorPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !Uri.IsHexDigit(character));
    }

    private void OnHexColorPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox || !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var pasted = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
        var normalized = new string(pasted.Where(Uri.IsHexDigit).Take(6).ToArray()).ToUpperInvariant();
        e.CancelCommand();
        textBox.Text = normalized;
        textBox.CaretIndex = normalized.Length;
    }

    private void OnDotColorChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDotColorSwatches();
        if (_settingsInitialized && TryReadDotColors(out _, out _, out _, out _))
        {
            UpdatePlacementPreview();
        }
    }

    private void OnClearDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        _diagnosticGenerationBaseline = _lastBaseDiagnostic?.Snapshot.Generation ?? 0;
        DiagnosticSummaryText.Text = "尚无观察记录。";
        DiagnosticText.Text = "尚未观察到外部应用。";
    }

    private void OnStepperClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
        {
            return;
        }

        var separatorIndex = tag.LastIndexOf(':');
        if (separatorIndex <= 0 ||
            !int.TryParse(tag[(separatorIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta))
        {
            return;
        }

        var appearance = GetAppearance(_editingStyle);
        var target = tag[..separatorIndex] switch
        {
            "DotSize" => (
                TextBox: DotSizeTextBox,
                Minimum: InputCueSettings.MinimumIndicatorSizeDip,
                Maximum: InputCueSettings.MaximumIndicatorSizeDip,
                Fallback: appearance.SizeDip),
            "BadgeSize" => (
                TextBox: BadgeSizeTextBox,
                Minimum: InputCueSettings.MinimumLightBadgeSizeDip,
                Maximum: InputCueSettings.MaximumLightBadgeSizeDip,
                Fallback: appearance.SizeDip),
            "Horizontal" => (
                TextBox: HorizontalOffsetTextBox,
                Minimum: InputCueSettings.MinimumOffsetDip,
                Maximum: InputCueSettings.MaximumOffsetDip,
                Fallback: appearance.HorizontalOffsetDip),
            "Vertical" => (
                TextBox: VerticalOffsetTextBox,
                Minimum: InputCueSettings.MinimumOffsetDip,
                Maximum: InputCueSettings.MaximumOffsetDip,
                Fallback: appearance.VerticalOffsetDip),
            _ => default,
        };
        if (target.TextBox is null)
        {
            return;
        }

        var currentValue = int.TryParse(
            target.TextBox.Text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedValue)
            ? parsedValue
            : target.Fallback;
        target.TextBox.Text = Math.Clamp(
                currentValue + delta,
                target.Minimum,
                target.Maximum)
            .ToString(CultureInfo.InvariantCulture);
    }

    private void OnPreviewLayoutChanged(object? sender, EventArgs e)
    {
        if (!_settingsInitialized)
        {
            return;
        }

        if (!IsVisible || WindowState is WindowState.Minimized)
        {
            _previewAllowed = false;
            PreviewIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        _previewAllowed = true;
        _ = Dispatcher.BeginInvoke(ShowPreviewOverlay, DispatcherPriority.Loaded);
    }

    private void OnPreviewVisibilityChanged(object? sender, EventArgs e)
    {
        if (!IsVisible || WindowState is WindowState.Minimized)
        {
            _previewAllowed = false;
            PreviewIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        _previewAllowed = true;
        ShowPreviewOverlay();
    }

    private void OnPreviewIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        OnPreviewVisibilityChanged(sender, EventArgs.Empty);

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
        RefreshWatchingStatus();
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
        RefreshWatchingStatus();
        ShowPreviewOverlay();
        StatusText.Text = "诊断探针已暂停。";
        WatchingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshWatchingStatus()
    {
        var isWatching = IsWatching;
        WatchingStatusText.Text = isWatching ? "正在监听" : "已暂停";
        WatchingStatusText.Foreground = isWatching
            ? (Brush)FindResource("MutedBrush")
            : (Brush)FindResource("PausedBrush");
        WatchingStatusDot.Fill = isWatching
            ? (Brush)FindResource("ListeningBrush")
            : (Brush)FindResource("PausedBrush");
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
                RefreshWatchingStatus();
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
            DateTimeOffset.UtcNow,
            diagnostic.IsPositionStabilization,
            diagnostic.SuppressContextReplay);
        if (_indicatorEnabled)
        {
            _overlayPresenter.Update(indicatorState);
        }
        else
        {
            _overlayPresenter.Hide();
        }

        UpdateIndicatorTimer(indicatorState, diagnostic.Snapshot.Eligibility);

        DiagnosticSummaryText.Text =
            $"最近观察 {diagnostic.Snapshot.ObservedAt.ToLocalTime():HH:mm:ss.fff} · " +
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

    private bool TryReadAppearance(
        IndicatorStyle style,
        out IndicatorAppearanceSettings appearance)
    {
        var fallback = GetAppearance(style);
        appearance = fallback;
        var sizeText = style == IndicatorStyle.Dot
            ? DotSizeTextBox.Text
            : BadgeSizeTextBox.Text;
        var minimumSize = style == IndicatorStyle.Dot
            ? InputCueSettings.MinimumIndicatorSizeDip
            : InputCueSettings.MinimumLightBadgeSizeDip;
        var maximumSize = style == IndicatorStyle.Dot
            ? InputCueSettings.MaximumIndicatorSizeDip
            : InputCueSettings.MaximumLightBadgeSizeDip;
        if (!TryReadBoundedInteger(
                HorizontalOffsetTextBox.Text,
                InputCueSettings.MinimumOffsetDip,
                InputCueSettings.MaximumOffsetDip,
                out var horizontalOffsetDip) ||
            !TryReadBoundedInteger(
                VerticalOffsetTextBox.Text,
                InputCueSettings.MinimumOffsetDip,
                InputCueSettings.MaximumOffsetDip,
                out var verticalOffsetDip) ||
            !TryReadBoundedInteger(sizeText, minimumSize, maximumSize, out var sizeDip))
        {
            return false;
        }

        appearance = new IndicatorAppearanceSettings(
            GetSelectedPlacement() ?? fallback.Placement,
            horizontalOffsetDip,
            verticalOffsetDip,
            sizeDip);
        return true;
    }

    private static bool TryReadBoundedInteger(
        string text,
        int minimum,
        int maximum,
        out int value) =>
        int.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value) &&
        value >= minimum &&
        value <= maximum;

    private bool TryReadDotColors(
        out string chinese,
        out string english,
        out string englishUs,
        out string capsLock)
    {
        chinese = ChineseDotColorTextBox.Text.ToUpperInvariant();
        english = EnglishDotColorTextBox.Text.ToUpperInvariant();
        englishUs = EnglishUsDotColorTextBox.Text.ToUpperInvariant();
        capsLock = CapsLockDotColorTextBox.Text.ToUpperInvariant();
        return InputCueSettings.IsHexColor(chinese) &&
            InputCueSettings.IsHexColor(english) &&
            InputCueSettings.IsHexColor(englishUs) &&
            InputCueSettings.IsHexColor(capsLock);
    }

    private void UpdateDotColorSwatches()
    {
        if (ChineseDotColorTextBox is null || EnglishDotColorTextBox is null ||
            EnglishUsDotColorTextBox is null || CapsLockDotColorTextBox is null)
        {
            return;
        }

        UpdateDotColorSwatch(ChineseDotColorTextBox, ChineseDotColorSwatch);
        UpdateDotColorSwatch(EnglishDotColorTextBox, EnglishDotColorSwatch);
        UpdateDotColorSwatch(EnglishUsDotColorTextBox, EnglishUsDotColorSwatch);
        UpdateDotColorSwatch(CapsLockDotColorTextBox, CapsLockDotColorSwatch);
        UpdateDotColorSwatch(ChineseDotColorTextBox, StyleDotPreview);
    }

    private static void UpdateDotColorSwatch(TextBox textBox, System.Windows.Shapes.Shape swatch)
    {
        if (InputCueSettings.IsHexColor(textBox.Text))
        {
            swatch.Fill = (Brush)new BrushConverter().ConvertFromString($"#{textBox.Text}")!;
        }
    }

    private IndicatorPlacement? GetSelectedPlacement()
    {
        foreach (var radioButton in PositionPicker.Children.OfType<RadioButton>())
        {
            if (radioButton.IsChecked is true &&
                radioButton.Tag is string value &&
                Enum.TryParse<IndicatorPlacement>(value, out var placement))
            {
                return placement;
            }
        }

        return null;
    }

    private IndicatorStyle GetSelectedStyle() =>
        ShadowBadgeStyleRadio.IsChecked is true
            ? IndicatorStyle.ShadowBadge
            : LightBadgeStyleRadio.IsChecked is true
                ? IndicatorStyle.LightBadge
                : IndicatorStyle.Dot;

    private void SetStyleSelection(IndicatorStyle style)
    {
        DotStyleRadio.IsChecked = style == IndicatorStyle.Dot;
        LightBadgeStyleRadio.IsChecked = style == IndicatorStyle.LightBadge;
        ShadowBadgeStyleRadio.IsChecked = style == IndicatorStyle.ShadowBadge;
    }

    private IndicatorAppearanceSettings GetAppearance(IndicatorStyle style) => style switch
    {
        IndicatorStyle.Dot => _dotAppearance,
        IndicatorStyle.LightBadge => _lightBadgeAppearance,
        IndicatorStyle.ShadowBadge => _shadowBadgeAppearance,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
    };

    private void SetAppearance(IndicatorStyle style, IndicatorAppearanceSettings appearance)
    {
        switch (style)
        {
            case IndicatorStyle.Dot:
                _dotAppearance = appearance;
                break;
            case IndicatorStyle.LightBadge:
                _lightBadgeAppearance = appearance;
                break;
            case IndicatorStyle.ShadowBadge:
                _shadowBadgeAppearance = appearance;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(style), style, null);
        }
    }

    private void LoadAppearance(IndicatorStyle style)
    {
        var appearance = GetAppearance(style);
        SetPlacementSelection(appearance.Placement);
        if (style == IndicatorStyle.Dot)
        {
            DotSizeTextBox.Text = appearance.SizeDip.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            BadgeSizeTextBox.Text = appearance.SizeDip.ToString(CultureInfo.InvariantCulture);
        }

        HorizontalOffsetTextBox.Text =
            appearance.HorizontalOffsetDip.ToString(CultureInfo.InvariantCulture);
        VerticalOffsetTextBox.Text =
            appearance.VerticalOffsetDip.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateStylePanels()
    {
        var style = GetSelectedStyle();
        DotSizePanel.Visibility = style == IndicatorStyle.Dot
            ? Visibility.Visible
            : Visibility.Collapsed;
        BadgeSizePanel.Visibility = style is IndicatorStyle.LightBadge or IndicatorStyle.ShadowBadge
            ? Visibility.Visible
            : Visibility.Collapsed;
        DotColorPanel.Visibility = style == IndicatorStyle.Dot
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetPlacementSelection(IndicatorPlacement placement)
    {
        foreach (var radioButton in PositionPicker.Children.OfType<RadioButton>())
        {
            radioButton.IsChecked =
                radioButton.Tag is string value &&
                Enum.TryParse<IndicatorPlacement>(value, out var candidate) &&
                candidate == placement;
        }
    }

    private void UpdatePlacementPreview()
    {
        var style = GetSelectedStyle();
        var fallback = GetAppearance(style);
        var placement = GetSelectedPlacement() ?? fallback.Placement;
        var sizeText = style == IndicatorStyle.Dot
            ? DotSizeTextBox.Text
            : BadgeSizeTextBox.Text;
        var minimumSize = style == IndicatorStyle.Dot
            ? InputCueSettings.MinimumIndicatorSizeDip
            : InputCueSettings.MinimumLightBadgeSizeDip;
        var maximumSize = style == IndicatorStyle.Dot
            ? InputCueSettings.MaximumIndicatorSizeDip
            : InputCueSettings.MaximumLightBadgeSizeDip;
        var size = TryReadBoundedInteger(sizeText, minimumSize, maximumSize, out var parsedSize)
            ? parsedSize
            : fallback.SizeDip;
        var dotSize = style == IndicatorStyle.Dot ? size : _dotAppearance.SizeDip;
        var badgeSize = style == IndicatorStyle.Dot ? _lightBadgeAppearance.SizeDip : size;
        var horizontalOffset = TryReadBoundedInteger(
            HorizontalOffsetTextBox.Text,
            InputCueSettings.MinimumOffsetDip,
            InputCueSettings.MaximumOffsetDip,
            out var parsedHorizontalOffset)
            ? parsedHorizontalOffset
            : fallback.HorizontalOffsetDip;
        var verticalOffset = TryReadBoundedInteger(
            VerticalOffsetTextBox.Text,
            InputCueSettings.MinimumOffsetDip,
            InputCueSettings.MaximumOffsetDip,
            out var parsedVerticalOffset)
            ? parsedVerticalOffset
            : fallback.VerticalOffsetDip;
        if (!TryReadDotColors(
                out var chineseDotColor,
                out var englishDotColor,
                out var englishUsDotColor,
                out var capsLockDotColor))
        {
            chineseDotColor = _chineseDotColor;
            englishDotColor = _englishDotColor;
            englishUsDotColor = _englishUsDotColor;
            capsLockDotColor = _capsLockDotColor;
        }

        _overlayPresenter.Configure(
            style,
            placement,
            horizontalOffset,
            verticalOffset,
            dotSize,
            badgeSize,
            chineseDotColor,
            englishDotColor,
            englishUsDotColor,
            capsLockDotColor);
        PreviewIndicator.Configure(
            style,
            dotSize,
            badgeSize,
            chineseDotColor,
            englishDotColor,
            englishUsDotColor,
            capsLockDotColor);
        ShowPreviewOverlay();
    }

    private void ShowPreviewOverlay()
    {
        if (!_settingsInitialized || !_previewAllowed || !IsVisible ||
            WindowState is WindowState.Minimized ||
            !PreviewCaret.IsVisible || PreviewCaret.ActualWidth <= 0 || PreviewCaret.ActualHeight <= 0)
        {
            PreviewIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var inputState = _lastBaseDiagnostic?.Snapshot.InputState ?? InputState.Chinese;
        inputState = EffectiveInputState.Resolve(inputState, UiCapsLockProbe.IsEnabled());
        if (inputState is InputState.Unknown)
        {
            inputState = InputState.Chinese;
        }

        PreviewIndicator.Render(inputState);
        PreviewIndicator.Visibility = Visibility.Visible;
        PositionPreviewIndicator();
    }

    private void PositionPreviewIndicator()
    {
        const double gap = 6;
        var anchor = new Rect(
            PreviewCaret.TranslatePoint(new Point(), PreviewCanvas),
            PreviewCaret.RenderSize);
        var width = PreviewIndicator.Width;
        var height = PreviewIndicator.Height;
        var fallback = GetAppearance(GetSelectedStyle());
        var placement = GetSelectedPlacement() ?? fallback.Placement;
        var horizontalOffset = int.TryParse(HorizontalOffsetTextBox.Text, out var parsedHorizontal)
            ? parsedHorizontal
            : fallback.HorizontalOffsetDip;
        var verticalOffset = int.TryParse(VerticalOffsetTextBox.Text, out var parsedVertical)
            ? parsedVertical
            : fallback.VerticalOffsetDip;
        var centeredX = anchor.Left + ((anchor.Width - width) / 2);
        var centeredY = anchor.Top + ((anchor.Height - height) / 2);
        var position = placement switch
        {
            IndicatorPlacement.TopLeft => new Point(anchor.Left - gap - width, anchor.Top - gap - height),
            IndicatorPlacement.Top => new Point(centeredX, anchor.Top - gap - height),
            IndicatorPlacement.TopRight => new Point(anchor.Right + gap, anchor.Top - gap - height),
            IndicatorPlacement.Left => new Point(anchor.Left - gap - width, centeredY),
            IndicatorPlacement.Right => new Point(anchor.Right + gap, centeredY),
            IndicatorPlacement.BottomLeft => new Point(anchor.Left - gap - width, anchor.Bottom + gap),
            IndicatorPlacement.Bottom => new Point(centeredX, anchor.Bottom + gap),
            IndicatorPlacement.BottomRight => new Point(anchor.Right + gap, anchor.Bottom + gap),
            _ => new Point(anchor.Right + gap, anchor.Bottom + gap),
        };
        Canvas.SetLeft(
            PreviewIndicator,
            Math.Clamp(position.X + horizontalOffset, 0, Math.Max(0, PreviewCanvas.ActualWidth - width)));
        Canvas.SetTop(
            PreviewIndicator,
            Math.Clamp(position.Y + verticalOffset, 0, Math.Max(0, PreviewCanvas.ActualHeight - height)));
    }

    private void ConfigureOverlay()
    {
        var appearance = GetAppearance(_style);
        _overlayPresenter.Configure(
            _style,
            appearance.Placement,
            appearance.HorizontalOffsetDip,
            appearance.VerticalOffsetDip,
            _dotAppearance.SizeDip,
            _style == IndicatorStyle.Dot ? _lightBadgeAppearance.SizeDip : appearance.SizeDip,
            _chineseDotColor,
            _englishDotColor,
            _englishUsDotColor,
            _capsLockDotColor);
        PreviewIndicator.Configure(
            _style,
            _dotAppearance.SizeDip,
            _style == IndicatorStyle.Dot ? _lightBadgeAppearance.SizeDip : appearance.SizeDip,
            _chineseDotColor,
            _englishDotColor,
            _englishUsDotColor,
            _capsLockDotColor);
    }

    private static string StyleName(IndicatorStyle style) => style switch
    {
        IndicatorStyle.Dot => "圆点",
        IndicatorStyle.LightBadge => "描边",
        IndicatorStyle.ShadowBadge => "阴影",
        _ => "圆点",
    };

    private static string PlacementName(IndicatorPlacement placement) => placement switch
    {
        IndicatorPlacement.TopLeft => "左上",
        IndicatorPlacement.Top => "上方",
        IndicatorPlacement.TopRight => "右上",
        IndicatorPlacement.Left => "左侧",
        IndicatorPlacement.Right => "右侧",
        IndicatorPlacement.BottomLeft => "左下",
        IndicatorPlacement.Bottom => "下方",
        IndicatorPlacement.BottomRight => "右下",
        _ => "右侧",
    };

    private bool PersistSettings()
    {
        if (!_settingsInitialized)
        {
            return true;
        }

        var activeAppearance = GetAppearance(_style);
        return _saveSettings(new InputCueSettings(
            _indicatorEnabled,
            _displayDurationMilliseconds,
            _minimumDisplayDurationMilliseconds,
            activeAppearance.Placement,
            activeAppearance.HorizontalOffsetDip,
            activeAppearance.VerticalOffsetDip,
            _dotAppearance.SizeDip,
            _style,
            _lightBadgeAppearance.SizeDip,
            _chineseDotColor,
            _englishDotColor,
            _englishUsDotColor,
            _capsLockDotColor,
            _dotAppearance,
            _lightBadgeAppearance,
            _shadowBadgeAppearance));
    }

    private string FormatDiagnostic(InputContextDiagnostic diagnostic)
    {
        var snapshot = diagnostic.Snapshot;
        var displayedGeneration = Math.Max(0, snapshot.Generation - _diagnosticGenerationBaseline);
        var inputStateEvidence = diagnostic.InputStateEvidence ?? InputStateEvidence.Unavailable;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Generation        {displayedGeneration}
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
