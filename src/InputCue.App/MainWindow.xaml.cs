using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
    private const int PreviewGapDip = 6;
    private const int PreviewWindowPaddingDip = 6;
    private const double PreviewBadgeBaseSizeDip = 36;
    private const double PreviewBadgeBorderDip = 2.5;
    private const double PreviewBadgeInsetDip = 5;
    private const double PreviewBadgeShadowOffsetDip = 4;
    private const double PreviewWidthDip = 160;
    private const double PreviewHeightDip = 90;
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
    private IndicatorPlacement _placement;
    private int _horizontalOffsetDip;
    private int _verticalOffsetDip;
    private int _indicatorSizeDip;
    private IndicatorStyle _style;
    private int _lightBadgeSizeDip;

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
        _placement = settings.Placement;
        _horizontalOffsetDip = settings.HorizontalOffsetDip;
        _verticalOffsetDip = settings.VerticalOffsetDip;
        _indicatorSizeDip = settings.IndicatorSizeDip;
        _style = settings.Style;
        _lightBadgeSizeDip = settings.LightBadgeSizeDip;
        DisplayDurationTextBox.Text = _displayDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        MinimumDisplayDurationTextBox.Text =
            _minimumDisplayDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        DotSizeTextBox.Text = _indicatorSizeDip.ToString(CultureInfo.InvariantCulture);
        BadgeSizeTextBox.Text = _lightBadgeSizeDip.ToString(CultureInfo.InvariantCulture);
        HorizontalOffsetTextBox.Text = _horizontalOffsetDip.ToString(CultureInfo.InvariantCulture);
        VerticalOffsetTextBox.Text = _verticalOffsetDip.ToString(CultureInfo.InvariantCulture);
        SetStyleSelection(_style);
        SetPlacementSelection(_placement);
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
        _ = new WindowInteropHelper(this).EnsureHandle();
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
            if (!saved)
            {
                StatusText.Text = "提示已关闭，但设置未能保存。";
            }

            return;
        }

        if (_lastBaseDiagnostic is { } diagnostic)
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

        if (!TryReadPositionSettings(
                out var style,
                out var placement,
                out var horizontalOffsetDip,
                out var verticalOffsetDip,
                out var indicatorSizeDip,
                out var lightBadgeSizeDip))
        {
            StatusText.Text =
                $"圆点尺寸必须为 {InputCueSettings.MinimumIndicatorSizeDip} 到 " +
                $"{InputCueSettings.MaximumIndicatorSizeDip}；徽标尺寸必须为 " +
                $"{InputCueSettings.MinimumLightBadgeSizeDip} 到 " +
                $"{InputCueSettings.MaximumLightBadgeSizeDip}；横向和纵向微调必须为 " +
                $"{InputCueSettings.MinimumOffsetDip} 到 {InputCueSettings.MaximumOffsetDip}。";
            return;
        }

        _displayDurationMilliseconds = displayDuration;
        _minimumDisplayDurationMilliseconds = minimumDisplayDuration;
        _style = style;
        _placement = placement;
        _horizontalOffsetDip = horizontalOffsetDip;
        _verticalOffsetDip = verticalOffsetDip;
        _indicatorSizeDip = indicatorSizeDip;
        _lightBadgeSizeDip = lightBadgeSizeDip;
        _indicatorSession = CreateIndicatorSession(displayDuration, minimumDisplayDuration);
        _overlayPresenter.Hide();
        ConfigureOverlay();
        _indicatorTimer.Stop();
        var appliedSize = style == IndicatorStyle.Dot ? indicatorSizeDip : lightBadgeSizeDip;
        StatusText.Text = PersistSettings()
            ? $"已应用并保存：{StyleName(style)}，{PlacementName(placement)}，尺寸 {appliedSize}，" +
              $"微调 ({horizontalOffsetDip}, {verticalOffsetDip})。"
            : "设置已应用，但未能保存。";
    }

    private void OnStylePreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsInitialized)
        {
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
        SetPlacementSelection(IndicatorPlacement.Right);
        HorizontalOffsetTextBox.Text = "0";
        VerticalOffsetTextBox.Text = "0";
        UpdatePlacementPreview();
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

    private bool TryReadPositionSettings(
        out IndicatorStyle style,
        out IndicatorPlacement placement,
        out int horizontalOffsetDip,
        out int verticalOffsetDip,
        out int indicatorSizeDip,
        out int lightBadgeSizeDip)
    {
        style = GetSelectedStyle();
        placement = GetSelectedPlacement() ?? _placement;
        horizontalOffsetDip = 0;
        verticalOffsetDip = 0;
        indicatorSizeDip = 0;
        lightBadgeSizeDip = 0;
        return TryReadBoundedInteger(
                HorizontalOffsetTextBox.Text,
                InputCueSettings.MinimumOffsetDip,
                InputCueSettings.MaximumOffsetDip,
                out horizontalOffsetDip) &&
            TryReadBoundedInteger(
                VerticalOffsetTextBox.Text,
                InputCueSettings.MinimumOffsetDip,
                InputCueSettings.MaximumOffsetDip,
                out verticalOffsetDip) &&
            TryReadBoundedInteger(
                DotSizeTextBox.Text,
                InputCueSettings.MinimumIndicatorSizeDip,
                InputCueSettings.MaximumIndicatorSizeDip,
                out indicatorSizeDip) &&
            TryReadBoundedInteger(
                BadgeSizeTextBox.Text,
                InputCueSettings.MinimumLightBadgeSizeDip,
                InputCueSettings.MaximumLightBadgeSizeDip,
                out lightBadgeSizeDip);
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
        LightBadgeStyleRadio.IsChecked is true
            ? IndicatorStyle.LightBadge
            : IndicatorStyle.Dot;

    private void SetStyleSelection(IndicatorStyle style)
    {
        DotStyleRadio.IsChecked = style == IndicatorStyle.Dot;
        LightBadgeStyleRadio.IsChecked = style == IndicatorStyle.LightBadge;
    }

    private void UpdateStylePanels()
    {
        var style = GetSelectedStyle();
        DotSizePanel.Visibility = style == IndicatorStyle.Dot
            ? Visibility.Visible
            : Visibility.Collapsed;
        BadgeSizePanel.Visibility = style == IndicatorStyle.LightBadge
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
        var placement = GetSelectedPlacement() ?? _placement;
        var dotSize = TryReadBoundedInteger(
            DotSizeTextBox.Text,
            InputCueSettings.MinimumIndicatorSizeDip,
            InputCueSettings.MaximumIndicatorSizeDip,
            out var parsedSize)
            ? parsedSize
            : Math.Clamp(
                _indicatorSizeDip,
                InputCueSettings.MinimumIndicatorSizeDip,
                InputCueSettings.MaximumIndicatorSizeDip);
        var badgeSize = TryReadBoundedInteger(
            BadgeSizeTextBox.Text,
            InputCueSettings.MinimumLightBadgeSizeDip,
            InputCueSettings.MaximumLightBadgeSizeDip,
            out var parsedBadgeSize)
            ? parsedBadgeSize
            : Math.Clamp(
                _lightBadgeSizeDip,
                InputCueSettings.MinimumLightBadgeSizeDip,
                InputCueSettings.MaximumLightBadgeSizeDip);
        var horizontalOffset = TryReadBoundedInteger(
            HorizontalOffsetTextBox.Text,
            InputCueSettings.MinimumOffsetDip,
            InputCueSettings.MaximumOffsetDip,
            out var parsedHorizontalOffset)
            ? parsedHorizontalOffset
            : _horizontalOffsetDip;
        var verticalOffset = TryReadBoundedInteger(
            VerticalOffsetTextBox.Text,
            InputCueSettings.MinimumOffsetDip,
            InputCueSettings.MaximumOffsetDip,
            out var parsedVerticalOffset)
            ? parsedVerticalOffset
            : _verticalOffsetDip;

        const double caretLeft = 79;
        const double caretTop = 31;
        const double caretWidth = 2;
        const double caretHeight = 28;
        var badgeScale = badgeSize / PreviewBadgeBaseSizeDip;
        var badgeShadowOffset = PreviewBadgeShadowOffsetDip * badgeScale;
        var windowSize = style == IndicatorStyle.Dot
            ? dotSize + PreviewWindowPaddingDip
            : badgeSize + badgeShadowOffset;
        var centeredX = caretLeft + ((caretWidth - windowSize) / 2);
        var centeredY = caretTop + ((caretHeight - windowSize) / 2);
        var leftX = caretLeft - PreviewGapDip - windowSize;
        var rightX = caretLeft + caretWidth + PreviewGapDip;
        var topY = caretTop - PreviewGapDip - windowSize;
        var bottomY = caretTop + caretHeight + PreviewGapDip;

        var (windowX, windowY) = placement switch
        {
            IndicatorPlacement.TopLeft => (leftX, topY),
            IndicatorPlacement.Top => (centeredX, topY),
            IndicatorPlacement.TopRight => (rightX, topY),
            IndicatorPlacement.Left => (leftX, centeredY),
            IndicatorPlacement.Right => (rightX, centeredY),
            IndicatorPlacement.BottomLeft => (leftX, bottomY),
            IndicatorPlacement.Bottom => (centeredX, bottomY),
            IndicatorPlacement.BottomRight => (rightX, bottomY),
            _ => (rightX, centeredY),
        };
        windowX = Math.Clamp(windowX + horizontalOffset, 0, PreviewWidthDip - windowSize);
        windowY = Math.Clamp(windowY + verticalOffset, 0, PreviewHeightDip - windowSize);

        PlacementPreviewDot.Visibility = style == IndicatorStyle.Dot
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlacementPreviewBadge.Visibility = style == IndicatorStyle.LightBadge
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlacementPreviewDot.Width = dotSize;
        PlacementPreviewDot.Height = dotSize;
        Canvas.SetLeft(PlacementPreviewDot, windowX + (PreviewWindowPaddingDip / 2d));
        Canvas.SetTop(PlacementPreviewDot, windowY + (PreviewWindowPaddingDip / 2d));

        PlacementPreviewBadge.Width = windowSize;
        PlacementPreviewBadge.Height = windowSize;
        PlacementPreviewBadgeShadow.Width = badgeSize;
        PlacementPreviewBadgeShadow.Height = badgeSize;
        Canvas.SetLeft(PlacementPreviewBadgeShadow, badgeShadowOffset);
        Canvas.SetTop(PlacementPreviewBadgeShadow, badgeShadowOffset);
        PlacementPreviewBadgeBody.Width = badgeSize;
        PlacementPreviewBadgeBody.Height = badgeSize;
        PlacementPreviewBadgeBody.BorderThickness =
            new Thickness(PreviewBadgeBorderDip * badgeScale);
        PlacementPreviewBadgeViewbox.Margin =
            new Thickness(PreviewBadgeInsetDip * badgeScale);
        Canvas.SetLeft(PlacementPreviewBadge, windowX);
        Canvas.SetTop(PlacementPreviewBadge, windowY);
    }

    private void ConfigureOverlay() =>
        _overlayPresenter.Configure(
            _style,
            _placement,
            _horizontalOffsetDip,
            _verticalOffsetDip,
            _indicatorSizeDip,
            _lightBadgeSizeDip);

    private static string StyleName(IndicatorStyle style) => style switch
    {
        IndicatorStyle.Dot => "圆点",
        IndicatorStyle.LightBadge => "亮色徽标",
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

    private bool PersistSettings() =>
        !_settingsInitialized ||
        _saveSettings(new InputCueSettings(
            _indicatorEnabled,
            _displayDurationMilliseconds,
            _minimumDisplayDurationMilliseconds,
            _placement,
            _horizontalOffsetDip,
            _verticalOffsetDip,
            _indicatorSizeDip,
            _style,
            _lightBadgeSizeDip));

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
