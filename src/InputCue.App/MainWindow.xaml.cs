using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InputCue.Core.Indicator;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;
using InputCue.Overlay;
using InputCue.Update;
using InputCue.Windows.InputContext;
using InputCue.Windows.Interop;
using Microsoft.Win32;

namespace InputCue.App;

public partial class MainWindow : Window, IDisposable
{
    private const int HistoryCapacity = 200;
    private static readonly TimeSpan AnimationTickInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan CapsLockPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan WatchRecoveryDelay = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    private readonly InputContextEngine _engine = new();
    private IndicatorSession _indicatorSession;
    private readonly IndicatorOverlayPresenter _overlayPresenter = new();
    private readonly InputContextTraceBuffer _history = new(HistoryCapacity);
    private readonly AppStayPromptPolicy _appStayPromptPolicy = new();
    private readonly DispatcherTimer _indicatorTimer;
    private readonly DispatcherTimer _updateToastTimer;
    private readonly DispatcherTimer _updateEllipsisTimer;
    private string _updateEllipsisBaseText = string.Empty;
    private int _updateEllipsisStep;
    private readonly RawKeyboardInputMonitor _keyboardInputMonitor = new();
    private readonly Func<InputCueSettings, bool> _saveSettings;
    private readonly Func<bool, bool> _setStartWithWindows;
    private readonly Func<Task<PortableUpdateResult>> _checkForUpdates;
    private readonly Action _openGitHub;
    private InputContextDiagnostic? _lastBaseDiagnostic;
    private InputContextDiagnostic? _lastRawDiagnostic;
    private PointerClickObservation? _pointerClick;
    private CancellationTokenSource? _watchCancellation;
    private bool _capsLockEnabled;
    private bool _disposed;
    private AppStayPromptMode _sameAppPromptMode;
    private int _sameAppPromptDelaySeconds;
    private bool _fullScreenAutoPause;
    private bool _isFullScreenAutoPaused;
    private bool _isRecovering;
    private bool _settingsInitialized;
    private bool _updatingAppearanceControls;
    private bool _startupSettingInitialized;
    private bool _started;
    private int _displayDurationMilliseconds;
    private int _minimumDisplayDurationMilliseconds;
    private IndicatorStyle _style;
    private IndicatorStyle _editingStyle;
    private IndicatorAppearanceSettings _dotAppearance;
    private IndicatorAppearanceSettings _lightBadgeAppearance;
    private IndicatorAppearanceSettings _shadowBadgeAppearance;
    private IndicatorAppearanceSettings _customAppearance;
    private readonly string _customIconsDirectory;
    private CustomIconCatalogResult _customIconCatalogResult = CustomIconCatalogResult.Empty;
    private CustomIconImages _customIconImages = CustomIconImages.Empty;
    private CustomIconShadowMode _customShadow = CustomIconShadowMode.None;
    private string? _customIconMessage;
    private bool _customIconMessageIsError;
    private PreviewCell[] _previewCells = [];

    private static readonly InputState[] PreviewCellStates =
        [InputState.Chinese, InputState.English, InputState.EnglishUs, InputState.CapsLock];

    private readonly record struct PreviewCell(
        IndicatorPreviewControl Control,
        System.Windows.Controls.Canvas IconCanvas,
        System.Windows.Controls.Canvas BoxCanvas,
        System.Windows.Controls.Border Box,
        System.Windows.Shapes.Rectangle Caret);
    private long _diagnosticGenerationBaseline;
    private bool _previewAllowed;
    private string _chineseDotColor;
    private string _englishDotColor;
    private string _englishUsDotColor;
    private string _capsLockDotColor;

    public event EventHandler? WatchingStateChanged;

    public bool IsWatching => _watchCancellation is not null;

    public bool IsRecovering => IsWatching && _isRecovering;

    public bool IsFullScreenAutoPaused => IsWatching && _isFullScreenAutoPaused;

    public MainWindow(
        InputCueSettings settings,
        Func<InputCueSettings, bool> saveSettings,
        bool startWithWindows,
        Func<bool, bool> setStartWithWindows,
        Func<Task<PortableUpdateResult>> checkForUpdates,
        Action openGitHub,
        string customIconsDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(saveSettings);
        ArgumentNullException.ThrowIfNull(setStartWithWindows);
        ArgumentNullException.ThrowIfNull(checkForUpdates);
        ArgumentNullException.ThrowIfNull(openGitHub);
        ArgumentException.ThrowIfNullOrWhiteSpace(customIconsDirectory);
        _saveSettings = saveSettings;
        _setStartWithWindows = setStartWithWindows;
        _checkForUpdates = checkForUpdates;
        _openGitHub = openGitHub;
        _customIconsDirectory = customIconsDirectory;
        InitializeComponent();
        VersionText.Text = FormatVersion(typeof(MainWindow).Assembly.GetName().Version);
        _previewCells =
        [
            new(PreviewIndicatorChinese, PreviewIconCanvasChinese, PreviewCanvasChinese, PreviewBoxChinese, PreviewCaretChinese),
            new(PreviewIndicatorEnglish, PreviewIconCanvasEnglish, PreviewCanvasEnglish, PreviewBoxEnglish, PreviewCaretEnglish),
            new(PreviewIndicatorEnglishUs, PreviewIconCanvasEnglishUs, PreviewCanvasEnglishUs, PreviewBoxEnglishUs, PreviewCaretEnglishUs),
            new(PreviewIndicatorCapsLock, PreviewIconCanvasCapsLock, PreviewCanvasCapsLock, PreviewBoxCapsLock, PreviewCaretCapsLock),
        ];
        _sameAppPromptMode = settings.SameAppPromptMode;
        _sameAppPromptDelaySeconds = settings.SameAppPromptDelaySeconds;
        _fullScreenAutoPause = settings.FullScreenAutoPause;
        _displayDurationMilliseconds = settings.DisplayDurationMilliseconds;
        _minimumDisplayDurationMilliseconds = settings.MinimumDisplayDurationMilliseconds;
        _style = settings.Style;
        _editingStyle = _style;
        _dotAppearance = settings.GetAppearance(IndicatorStyle.Dot);
        _lightBadgeAppearance = settings.GetAppearance(IndicatorStyle.LightBadge);
        _shadowBadgeAppearance = settings.GetAppearance(IndicatorStyle.ShadowBadge);
        _customAppearance = settings.GetAppearance(IndicatorStyle.Custom);
        _customShadow = settings.CustomIconShadow;
        SetCustomShadowSelection(_customShadow);
        _chineseDotColor = settings.ChineseDotColor;
        _englishDotColor = settings.EnglishDotColor;
        _englishUsDotColor = settings.EnglishUsDotColor;
        _capsLockDotColor = settings.CapsLockDotColor;
        DisplayDurationTextBox.Text = _displayDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        MinimumDisplayDurationTextBox.Text =
            _minimumDisplayDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        SameAppPromptDelayTextBox.Text =
            _sameAppPromptDelaySeconds.ToString(CultureInfo.InvariantCulture);
        ChineseDotColorTextBox.Text = _chineseDotColor;
        EnglishDotColorTextBox.Text = _englishDotColor;
        EnglishUsDotColorTextBox.Text = _englishUsDotColor;
        CapsLockDotColorTextBox.Text = _capsLockDotColor;
        SetStyleSelection(_style);
        LoadAppearance(_editingStyle);
        UpdateStylePanels();
        RefreshCustomIcons(forceRescan: true);
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
        _updateToastTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _updateToastTimer.Tick += OnUpdateToastTimerTick;
        _updateEllipsisTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _updateEllipsisTimer.Tick += OnUpdateEllipsisTimerTick;
        _keyboardInputMonitor.EditingKeyPressed += OnEditingKeyPressed;
        _keyboardInputMonitor.PointerClickObserved += OnPointerClickObserved;
        _keyboardInputMonitor.PointerAnchorInvalidated += OnPointerAnchorInvalidated;
        SetSameAppPromptSelection(_sameAppPromptMode);
        FullScreenAutoPauseCheckBox.IsChecked = _fullScreenAutoPause;
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
        _updateToastTimer.Stop();
        _updateToastTimer.Tick -= OnUpdateToastTimerTick;
        _updateEllipsisTimer.Stop();
        _updateEllipsisTimer.Tick -= OnUpdateEllipsisTimerTick;
        _keyboardInputMonitor.EditingKeyPressed -= OnEditingKeyPressed;
        _keyboardInputMonitor.PointerClickObserved -= OnPointerClickObserved;
        _keyboardInputMonitor.PointerAnchorInvalidated -= OnPointerAnchorInvalidated;
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

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateButton.ToolTip = "正在检查更新…";
        StatusText.Text = "正在检查更新…";
        ShowUpdateNotice(PortableUpdateNotice.Checking);
        try
        {
            var result = await _checkForUpdates();
            StatusText.Text = result.Message;
            ShowUpdateNotice(PortableUpdateNotice.FromResult(result));
        }
        catch (OperationCanceledException)
        {
            HideUpdateNotice();
        }
        finally
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.ToolTip = "检查更新";
        }
    }

    internal void ShowUpdateNotice(PortableUpdateNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        Dispatcher.VerifyAccess();

        var (background, border, foreground) =
            notice.Tone switch
            {
                PortableUpdateNoticeTone.Information => ("EFF6FF", "BFDBFE", "1E3A5F"),
                PortableUpdateNoticeTone.Success => ("F0FDF4", "BBF7D0", "14532D"),
                PortableUpdateNoticeTone.Warning => ("FFFBEB", "FDE68A", "78350F"),
                PortableUpdateNoticeTone.Error => ("FEF2F2", "FECACA", "7F1D1D"),
                _ => throw new ArgumentOutOfRangeException(nameof(notice), notice.Tone, null),
            };

        _updateToastTimer.Stop();
        _updateEllipsisTimer.Stop();
        UpdateToast.Background = BrushFromHex(background);
        UpdateToast.BorderBrush = BrushFromHex(border);
        UpdateToastText.Foreground = BrushFromHex(foreground);
        UpdateToastText.Text = notice.Message;
        UpdateToast.Visibility = Visibility.Visible;
        UpdateToast.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        UpdateToastTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

        if (ReferenceEquals(notice, PortableUpdateNotice.Checking))
        {
            // Animate the trailing ellipsis so a slow check never reads as frozen.
            _updateEllipsisBaseText = UpdateToastText.Text.TrimEnd('.', '…');
            _updateEllipsisStep = 0;
            UpdateToastText.Text = _updateEllipsisBaseText + ".";
            _updateEllipsisTimer.Start();
        }

        if (notice.Duration > TimeSpan.Zero)
        {
            _updateToastTimer.Interval = notice.Duration;
            _updateToastTimer.Start();
        }
    }

    private void OnUpdateEllipsisTimerTick(object? sender, EventArgs e)
    {
        _updateEllipsisStep = (_updateEllipsisStep + 1) % 3;
        UpdateToastText.Text = _updateEllipsisBaseText + new string('.', _updateEllipsisStep + 1);
    }

    private void OnUpdateToastTimerTick(object? sender, EventArgs e)
    {
        _updateToastTimer.Stop();
        HideUpdateNotice();
    }

    private void HideUpdateNotice()
    {
        _updateToastTimer.Stop();
        _updateEllipsisTimer.Stop();
        var animation = new DoubleAnimation(
            UpdateToast.Opacity,
            0,
            TimeSpan.FromMilliseconds(140));
        animation.Completed += (_, _) => UpdateToast.Visibility = Visibility.Collapsed;
        UpdateToast.BeginAnimation(OpacityProperty, animation);
    }

    private static SolidColorBrush BrushFromHex(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString($"#{value}"));
        brush.Freeze();
        return brush;
    }

    private void OnGitHubClick(object sender, RoutedEventArgs e) => _openGitHub();

    private void OnSameAppPromptModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_settingsInitialized)
        {
            return;
        }

        _sameAppPromptMode = GetSelectedSameAppPromptMode();
        _appStayPromptPolicy.Reset();
        StatusText.Text = PersistSettings()
            ? SameAppPromptModeStatusText(_sameAppPromptMode)
            : "同应用提示设置未能保存。";
    }

    private void OnSameAppPromptDelayTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingsInitialized ||
            !int.TryParse(
                SameAppPromptDelayTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var delaySeconds))
        {
            return;
        }

        if (delaySeconds > InputCueSettings.MaximumSameAppPromptDelaySeconds)
        {
            SameAppPromptDelayTextBox.Text =
                InputCueSettings.MaximumSameAppPromptDelaySeconds.ToString(CultureInfo.InvariantCulture);
            SameAppPromptDelayTextBox.CaretIndex = SameAppPromptDelayTextBox.Text.Length;
            delaySeconds = InputCueSettings.MaximumSameAppPromptDelaySeconds;
        }

        if (delaySeconds < InputCueSettings.MinimumSameAppPromptDelaySeconds ||
            delaySeconds == _sameAppPromptDelaySeconds)
        {
            return;
        }

        _sameAppPromptDelaySeconds = delaySeconds;
        if (!PersistSettings())
        {
            StatusText.Text = "同应用提示延时未能保存。";
        }
    }

    private AppStayPromptMode GetSelectedSameAppPromptMode() =>
        SameAppPromptAlwaysRadio.IsChecked is true
            ? AppStayPromptMode.Always
            : SameAppPromptDelayRadio.IsChecked is true
                ? AppStayPromptMode.AfterDelay
                : AppStayPromptMode.Never;

    private void SetSameAppPromptSelection(AppStayPromptMode mode)
    {
        SameAppPromptAlwaysRadio.IsChecked = mode == AppStayPromptMode.Always;
        SameAppPromptDelayRadio.IsChecked = mode == AppStayPromptMode.AfterDelay;
        SameAppPromptNeverRadio.IsChecked = mode == AppStayPromptMode.Never;
    }

    private string SameAppPromptModeStatusText(AppStayPromptMode mode) => mode switch
    {
        AppStayPromptMode.Always => "同应用内切换输入框时始终显示提示。",
        AppStayPromptMode.AfterDelay =>
            $"同应用内切换输入框时，{_sameAppPromptDelaySeconds} 秒后重新显示提示。",
        _ => "同应用内切换输入框时不再显示提示。",
    };

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        _appStayPromptPolicy.Reset();
        UpdateFullScreenAutoPauseState();
        RefreshCustomIcons(forceRescan: false);
        OnPreviewLayoutChanged(sender, e);
    }

    private void OnFullScreenAutoPauseChanged(object sender, RoutedEventArgs e)
    {
        if (!_settingsInitialized)
        {
            return;
        }

        _fullScreenAutoPause = FullScreenAutoPauseCheckBox.IsChecked is true;
        UpdateFullScreenAutoPauseState();
        StatusText.Text = PersistSettings()
            ? _fullScreenAutoPause
                ? "已启用全屏自动暂停。"
                : "已关闭全屏自动暂停。"
            : "全屏自动暂停设置未能保存。";
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
            _updatingAppearanceControls = true;
            try
            {
                LoadAppearance(_editingStyle);
            }
            finally
            {
                _updatingAppearanceControls = false;
            }

            UpdateStylePanels();
            ApplyAndPersistAppearancePreview();
        }
    }

    private void OnPlacementPreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsInitialized && !_updatingAppearanceControls)
        {
            ApplyAndPersistAppearancePreview();
        }
    }

    private void OnResetPlacementClick(object sender, RoutedEventArgs e)
    {
        var style = GetSelectedStyle();
        _updatingAppearanceControls = true;
        try
        {
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
        }
        finally
        {
            _updatingAppearanceControls = false;
        }

        ApplyAndPersistAppearancePreview();
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
        if (_settingsInitialized && !_updatingAppearanceControls &&
            TryReadDotColors(out _, out _, out _, out _))
        {
            ApplyAndPersistAppearancePreview();
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Operation messages are transient: any further interaction dismisses them.
        // Scan-derived invalid details stay until the offending file is fixed.
        if (_customIconMessage is null)
        {
            return;
        }

        _customIconMessage = null;
        UpdateCustomIconHint();
    }

    private void OnPickCustomIconClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<InputState>(tag, out var state) ||
            state == InputState.Unknown)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "PNG 图片 (*.png)|*.png",
            Title = $"选择{StateDisplayName(state)}的自定义图标",
        };
        if (dialog.ShowDialog(this) is not true)
        {
            return;
        }

        var validation = CustomIconCatalog.Validate(dialog.FileName);
        if (!validation.IsValid)
        {
            SetCustomIconMessage(
                $"图标未能应用：{CustomIconReasonText(validation.InvalidReason)}。",
                isError: true);
            return;
        }

        try
        {
            Directory.CreateDirectory(_customIconsDirectory);
            File.Copy(dialog.FileName, CustomIconFilePath(state), overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetCustomIconMessage("图标未能应用：图标文件夹不可写。", isError: true);
            return;
        }

        RefreshCustomIcons(forceRescan: true);
        SetCustomIconMessage($"已更新{StateDisplayName(state)}的自定义图标。", isError: false);
    }

    private void OnClearCustomIconClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<InputState>(tag, out var state) ||
            state == InputState.Unknown)
        {
            return;
        }

        try
        {
            if (File.Exists(CustomIconFilePath(state)))
            {
                File.Delete(CustomIconFilePath(state));
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetCustomIconMessage("清除失败：文件被占用或不可写。", isError: true);
            return;
        }

        RefreshCustomIcons(forceRescan: true);
        SetCustomIconMessage(
            $"已清除{StateDisplayName(state)}的自定义图标，恢复使用内置图标。",
            isError: false);
    }

    private void OnOpenIconsFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_customIconsDirectory);
            var readmePath = Path.Combine(_customIconsDirectory, "README.txt");
            if (!File.Exists(readmePath))
            {
                File.WriteAllText(readmePath, CustomIconsReadmeText, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetCustomIconMessage("无法创建图标文件夹。", isError: true);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _customIconsDirectory,
            UseShellExecute = true,
        });
    }

    private void OnRescanIconsClick(object sender, RoutedEventArgs e)
    {
        RefreshCustomIcons(forceRescan: true);
        SetCustomIconMessage("已重新扫描图标文件夹。", isError: false);
    }

    private void OnCustomShadowChanged(object sender, RoutedEventArgs e)
    {
        if (!_settingsInitialized ||
            sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse(tag, out CustomIconShadowMode mode) ||
            !Enum.IsDefined(mode))
        {
            return;
        }

        _customShadow = mode;
        ConfigureOverlay();
        SetCustomIconMessage(
            mode == CustomIconShadowMode.None
                ? "已关闭自定义图标阴影。"
                : $"自定义图标阴影已切换为{CustomShadowName(mode)}。",
            isError: false);
        if (!PersistSettings())
        {
            SetCustomIconMessage("设置已应用，但未能保存。", isError: true);
        }
    }

    private void SetCustomShadowSelection(CustomIconShadowMode mode)
    {
        CustomShadowNoneRadio.IsChecked = mode == CustomIconShadowMode.None;
        CustomShadowLightRadio.IsChecked = mode == CustomIconShadowMode.Light;
        CustomShadowHeavyRadio.IsChecked = mode == CustomIconShadowMode.Heavy;
        CustomShadowSolidRadio.IsChecked = mode == CustomIconShadowMode.Solid;
    }

    private static string CustomShadowName(CustomIconShadowMode mode) => mode switch
    {
        CustomIconShadowMode.Light => "轻",
        CustomIconShadowMode.Heavy => "重",
        CustomIconShadowMode.Solid => "实心",
        _ => "不显示",
    };

    private void RefreshCustomIcons(bool forceRescan)
    {
        var result = CustomIconCatalog.Scan(_customIconsDirectory);
        if (!forceRescan && result.Equals(_customIconCatalogResult))
        {
            return;
        }

        _customIconCatalogResult = result;
        _customIconImages = CustomIconImages.From(result);
        _overlayPresenter.UpdateCustomIcons(_customIconImages);
        foreach (var cell in _previewCells)
        {
            cell.Control.UpdateCustomIcons(_customIconImages);
        }

        UpdateCustomIconRows();
        ShowPreviewOverlay();
    }

    private void UpdateCustomIconRows()
    {
        UpdateCustomIconRow(
            _customIconCatalogResult.Chinese,
            ChineseIconThumbnail,
            ChineseIconPlaceholder,
            ChineseIconStatus,
            ClearChineseIconButton);
        UpdateCustomIconRow(
            _customIconCatalogResult.English,
            EnglishIconThumbnail,
            EnglishIconPlaceholder,
            EnglishIconStatus,
            ClearEnglishIconButton);
        UpdateCustomIconRow(
            _customIconCatalogResult.EnglishUs,
            EnglishUsIconThumbnail,
            EnglishUsIconPlaceholder,
            EnglishUsIconStatus,
            ClearEnglishUsIconButton);
        UpdateCustomIconRow(
            _customIconCatalogResult.CapsLock,
            CapsLockIconThumbnail,
            CapsLockIconPlaceholder,
            CapsLockIconStatus,
            ClearCapsLockIconButton);
        UpdateCustomIconHint();
    }

    private void UpdateCustomIconHint()
    {
        var invalid = new List<string>();
        AppendInvalidIconDescription(InputState.Chinese, _customIconCatalogResult.Chinese, invalid);
        AppendInvalidIconDescription(InputState.English, _customIconCatalogResult.English, invalid);
        AppendInvalidIconDescription(InputState.EnglishUs, _customIconCatalogResult.EnglishUs, invalid);
        AppendInvalidIconDescription(InputState.CapsLock, _customIconCatalogResult.CapsLock, invalid);
        if (invalid.Count > 0)
        {
            ShowCustomIconHint(string.Join("；", invalid) + "。", isError: true);
            return;
        }

        if (_customIconMessage is not null)
        {
            ShowCustomIconHint(_customIconMessage, _customIconMessageIsError);
            return;
        }

        ShowCustomIconHint(CustomIconHintText, isError: false);
    }

    private void ShowCustomIconHint(string text, bool isError)
    {
        CustomIconHint.Text = text;
        if (isError)
        {
            CustomIconHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));
            return;
        }

        CustomIconHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
    }

    private void SetCustomIconMessage(string text, bool isError)
    {
        _customIconMessage = text;
        _customIconMessageIsError = isError;
        UpdateCustomIconHint();
    }

    private static void AppendInvalidIconDescription(
        InputState state,
        CustomIconSlotResult slot,
        List<string> descriptions)
    {
        if (slot.Status == CustomIconSlotStatus.Invalid)
        {
            descriptions.Add(
                $"{StateDisplayName(state)}的图标无效：{CustomIconReasonText(slot.InvalidReason)}");
        }
    }

    private static void UpdateCustomIconRow(
        CustomIconSlotResult slot,
        System.Windows.Controls.Image thumbnail,
        TextBlock placeholder,
        TextBlock status,
        Button clearButton)
    {
        if (slot.IsValid)
        {
            thumbnail.Source = CreateIconThumbnail(slot.FilePath);
            thumbnail.Visibility = thumbnail.Source is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            placeholder.Visibility = thumbnail.Source is null
                ? Visibility.Visible
                : Visibility.Collapsed;
            status.Text = $"已设置（{slot.PixelWidth}×{slot.PixelHeight}）";
            status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            clearButton.IsEnabled = true;
            return;
        }

        thumbnail.Source = null;
        thumbnail.Visibility = Visibility.Collapsed;
        placeholder.Visibility = Visibility.Visible;
        clearButton.IsEnabled = slot.Status == CustomIconSlotStatus.Invalid;
        if (slot.Status == CustomIconSlotStatus.Invalid)
        {
            status.Text = "无效";
            status.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));
            return;
        }

        status.Text = "使用内置图标";
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
    }

    private static BitmapImage? CreateIconThumbnail(string? filePath)
    {
        if (filePath is null)
        {
            return null;
        }

        try
        {
            // Byte-stream decode, same as the indicator: avoids WPF's URI-keyed
            // bitmap cache serving stale frames after the file is replaced.
            using var stream = new MemoryStream(File.ReadAllBytes(filePath));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelWidth = 64;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                FileFormatException)
        {
            return null;
        }
    }

    private string CustomIconFilePath(InputState state) => Path.Combine(
        _customIconsDirectory,
        state switch
        {
            InputState.Chinese => CustomIconCatalog.ChineseFileName,
            InputState.English => CustomIconCatalog.EnglishFileName,
            InputState.EnglishUs => CustomIconCatalog.EnglishUsFileName,
            InputState.CapsLock => CustomIconCatalog.CapsLockFileName,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        });

    private static string StateDisplayName(InputState state) => state switch
    {
        InputState.Chinese => "中文",
        InputState.English => "输入法英文",
        InputState.EnglishUs => "美式键盘",
        InputState.CapsLock => "大写锁定",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static string CustomIconReasonText(CustomIconInvalidReason reason) => reason switch
    {
        CustomIconInvalidReason.FileTooLarge => $"文件超过 {CustomIconCatalog.MaxFileBytes / (1024 * 1024)} MB",
        CustomIconInvalidReason.DimensionsTooLarge => $"图片尺寸超过 {CustomIconCatalog.MaxPixelDimension}×{CustomIconCatalog.MaxPixelDimension}",
        CustomIconInvalidReason.JpegNotPng => "文件实际是 JPEG 格式，请另存为 PNG 后重试",
        _ => "不是有效的 PNG 文件",
    };

    private const string CustomIconHintText = "请选择PNG图片，推荐256x256尺寸；或者直接在文件夹里替换，然后重新扫描。";

    private const string CustomIconsReadmeText = """"
        InputCue 自定义图标
        ==================

        把 PNG 图片放入本文件夹并按下列名称命名，即可替换对应状态的提示图标：

          chinese.png      中文
          ime-english.png  输入法英文
          us-english.png   美式键盘（ENG）
          caps-lock.png    大写锁定

        说明：
        - 建议使用不小于 256×256 的 PNG；删除某个文件即恢复该状态的内置图标。
        - 无法解析、大于 10 MB 或超过 4096×4096 的文件不会生效，设置页会给出原因。
        - 直接修改本文件夹后，回到设置页点击「重新扫描」或重启 InputCue。
        """";

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
            SetPreviewCellsVisibility(Visibility.Collapsed);
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
            SetPreviewCellsVisibility(Visibility.Collapsed);
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
        _isRecovering = false;
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
        _isRecovering = false;
        _isFullScreenAutoPaused = false;
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
        WatchingStatusText.Text = !isWatching
            ? "已暂停"
            : _isRecovering
                ? "正在恢复"
                : _isFullScreenAutoPaused
                    ? "全屏暂停"
                : "正在监听";
        WatchingStatusText.Foreground = isWatching && !_isRecovering && !_isFullScreenAutoPaused
            ? (Brush)FindResource("MutedBrush")
            : (Brush)FindResource("PausedBrush");
        WatchingStatusDot.Fill = isWatching && !_isRecovering && !_isFullScreenAutoPaused
            ? (Brush)FindResource("ListeningBrush")
            : (Brush)FindResource("PausedBrush");
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
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
                return;
            }
            catch (Exception)
            {
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() => SetRecoveryState(isRecovering: true));
            try
            {
                await Task.Delay(WatchRecoveryDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() => SetRecoveryState(isRecovering: false));
        }
    }

    private void SetRecoveryState(bool isRecovering)
    {
        _isRecovering = isRecovering;
        if (isRecovering)
        {
            _overlayPresenter.Hide();
            _indicatorTimer.Stop();
            StatusText.Text = "监听意外中断，正在自动恢复。";
        }
        else
        {
            StatusText.Text = "监听已自动恢复。";
        }

        RefreshWatchingStatus();
        WatchingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowDiagnostic(InputContextDiagnostic diagnostic)
    {
        _lastRawDiagnostic = diagnostic;
        UpdateFullScreenAutoPauseState();
        if (_isFullScreenAutoPaused)
        {
            _lastBaseDiagnostic = diagnostic;
            return;
        }

        var effectiveDiagnostic = PointerAnchorFallbackPolicy.Apply(
            diagnostic,
            _pointerClick,
            DateTimeOffset.UtcNow,
            NativeMethods.GetForegroundWindow());
        _lastBaseDiagnostic = effectiveDiagnostic;
        _capsLockEnabled = UiCapsLockProbe.IsEnabled();
        PresentDiagnostic(ApplyCapsLock(effectiveDiagnostic, _capsLockEnabled));
    }

    private void OnPointerClickObserved(PointerClickObservation click)
    {
        if (_isFullScreenAutoPaused)
        {
            return;
        }

        _pointerClick = click;
        if (_watchCancellation is not null &&
            !IsActive &&
            _lastRawDiagnostic is { } diagnostic)
        {
            ShowDiagnostic(diagnostic);
        }
    }

    private void OnPointerAnchorInvalidated(object? sender, EventArgs e)
    {
        _pointerClick = null;
        if (_lastRawDiagnostic is { } rawDiagnostic)
        {
            _lastBaseDiagnostic = rawDiagnostic;
        }
    }

    private void PresentDiagnostic(InputContextDiagnostic diagnostic)
    {
        _history.Add(diagnostic);
        if (_isFullScreenAutoPaused)
        {
            _overlayPresenter.Hide();
            _indicatorTimer.Stop();
            return;
        }

        var suppressContextReplay = diagnostic.SuppressContextReplay ||
            _appStayPromptPolicy.ShouldSuppressContextReplay(
                _sameAppPromptMode,
                TimeSpan.FromSeconds(_sameAppPromptDelaySeconds),
                diagnostic.Target,
                diagnostic.Snapshot,
                DateTimeOffset.UtcNow);
        var indicatorState = _indicatorSession.Observe(
            diagnostic.Snapshot,
            DateTimeOffset.UtcNow,
            diagnostic.IsPositionStabilization,
            suppressContextReplay);
        _overlayPresenter.Update(indicatorState);

        UpdateIndicatorTimer(indicatorState, diagnostic.Snapshot.Eligibility);

        DiagnosticSummaryText.Text =
            $"最近观察 {diagnostic.Snapshot.ObservedAt.ToLocalTime():HH:mm:ss.fff} · " +
            $"耗时 {diagnostic.DurationMilliseconds:F1} ms";
        DiagnosticText.Text = FormatDiagnostic(diagnostic);
    }

    private void OnIndicatorTick(object? sender, EventArgs e)
    {
        if (_isFullScreenAutoPaused)
        {
            _indicatorTimer.Stop();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var capsLockEnabled = UiCapsLockProbe.IsEnabled();
        if (capsLockEnabled != _capsLockEnabled)
        {
            _capsLockEnabled = capsLockEnabled;
            if (!IsActive && _lastRawDiagnostic is { } rawDiagnostic)
            {
                var effectiveDiagnostic = PointerAnchorFallbackPolicy.Apply(
                    rawDiagnostic,
                    _pointerClick,
                    now,
                    NativeMethods.GetForegroundWindow());
                _lastBaseDiagnostic = effectiveDiagnostic;
                PresentDiagnostic(ApplyCapsLock(effectiveDiagnostic, capsLockEnabled, now));
                return;
            }
        }

        var indicatorState = _indicatorSession.Advance(now);
        _overlayPresenter.Update(indicatorState);

        UpdateIndicatorTimer(
            indicatorState,
            _lastBaseDiagnostic?.Snapshot.Eligibility ?? Eligibility.Unknown);
    }

    private void OnEditingKeyPressed(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var eligibility = _lastBaseDiagnostic?.Snapshot.Eligibility ?? Eligibility.Unknown;
        var retainPointerClick = PointerAnchorFallbackPolicy.ShouldRetainAfterEditingKey(
                _pointerClick,
                now,
                NativeMethods.GetForegroundWindow());
        if (!retainPointerClick)
        {
            _pointerClick = null;
            if (_lastRawDiagnostic is { } rawDiagnostic)
            {
                _lastBaseDiagnostic = rawDiagnostic;
            }
        }

        if (_isFullScreenAutoPaused ||
            IsActive ||
            eligibility is not (Eligibility.EditableCaret or Eligibility.EditableSelection))
        {
            return;
        }

        var wasVisible = _indicatorSession.Current.IsVisible;
        var indicatorState = _indicatorSession.ObserveInputActivity(now);
        if (!wasVisible)
        {
            return;
        }

        _overlayPresenter.Update(indicatorState);
        UpdateIndicatorTimer(indicatorState, eligibility);
    }

    private void UpdateIndicatorTimer(IndicatorViewState indicatorState, Eligibility eligibility)
    {
        if (_watchCancellation is null || _isFullScreenAutoPaused)
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

    private void UpdateFullScreenAutoPauseState()
    {
        var shouldPause = _fullScreenAutoPause &&
            !IsActive &&
            FullScreenWindowDetector.IsForegroundWindowFullScreen();
        if (shouldPause == _isFullScreenAutoPaused)
        {
            return;
        }

        _isFullScreenAutoPaused = shouldPause;
        if (shouldPause)
        {
            _pointerClick = null;
            _overlayPresenter.Hide();
            _indicatorTimer.Stop();
        }

        RefreshWatchingStatus();
        WatchingStateChanged?.Invoke(this, EventArgs.Empty);
    }

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

    private void ApplyAndPersistAppearancePreview()
    {
        var style = GetSelectedStyle();
        if (!TryReadAppearance(style, out var appearance) ||
            !TryReadDotColors(
                out var chineseDotColor,
                out var englishDotColor,
                out var englishUsDotColor,
                out var capsLockDotColor))
        {
            UpdatePlacementPreview();
            return;
        }

        _style = style;
        _editingStyle = style;
        SetAppearance(style, appearance);
        _chineseDotColor = chineseDotColor;
        _englishDotColor = englishDotColor;
        _englishUsDotColor = englishUsDotColor;
        _capsLockDotColor = capsLockDotColor;
        UpdatePlacementPreview();
        if (!PersistSettings())
        {
            StatusText.Text = "外观已应用，但未能保存。";
        }
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
        CustomStyleRadio.IsChecked is true
            ? IndicatorStyle.Custom
            : ShadowBadgeStyleRadio.IsChecked is true
                ? IndicatorStyle.ShadowBadge
                : LightBadgeStyleRadio.IsChecked is true
                    ? IndicatorStyle.LightBadge
                    : IndicatorStyle.Dot;

    private void SetStyleSelection(IndicatorStyle style)
    {
        DotStyleRadio.IsChecked = style == IndicatorStyle.Dot;
        LightBadgeStyleRadio.IsChecked = style == IndicatorStyle.LightBadge;
        ShadowBadgeStyleRadio.IsChecked = style == IndicatorStyle.ShadowBadge;
        CustomStyleRadio.IsChecked = style == IndicatorStyle.Custom;
    }

    private IndicatorAppearanceSettings GetAppearance(IndicatorStyle style) => style switch
    {
        IndicatorStyle.Dot => _dotAppearance,
        IndicatorStyle.LightBadge => _lightBadgeAppearance,
        IndicatorStyle.ShadowBadge => _shadowBadgeAppearance,
        IndicatorStyle.Custom => _customAppearance,
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
            case IndicatorStyle.Custom:
                _customAppearance = appearance;
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
        BadgeSizePanel.Visibility = style is IndicatorStyle.LightBadge or IndicatorStyle.ShadowBadge or IndicatorStyle.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;
        BadgeSizeLabel.Text = style == IndicatorStyle.Custom
            ? "图标尺寸"
            : "徽标尺寸";
        DotColorPanel.Visibility = style == IndicatorStyle.Dot
            ? Visibility.Visible
            : Visibility.Collapsed;
        CustomIconPanel.Visibility = style == IndicatorStyle.Custom
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
        PreviewIndicatorChinese.Configure(
            style,
            dotSize,
            badgeSize,
            chineseDotColor,
            englishDotColor,
            englishUsDotColor,
            capsLockDotColor);
        PreviewIndicatorEnglish.Configure(
            style,
            dotSize,
            badgeSize,
            chineseDotColor,
            englishDotColor,
            englishUsDotColor,
            capsLockDotColor);
        PreviewIndicatorEnglishUs.Configure(
            style,
            dotSize,
            badgeSize,
            chineseDotColor,
            englishDotColor,
            englishUsDotColor,
            capsLockDotColor);
        PreviewIndicatorCapsLock.Configure(
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
            !PreviewCaretChinese.IsVisible ||
            PreviewCaretChinese.ActualWidth <= 0 || PreviewCaretChinese.ActualHeight <= 0)
        {
            SetPreviewCellsVisibility(Visibility.Collapsed);
            return;
        }

        for (var index = 0; index < _previewCells.Length; index++)
        {
            var cell = _previewCells[index];
            cell.Control.Render(PreviewCellStates[index]);
            cell.Control.Visibility = Visibility.Visible;
            PositionCellPreview(cell);
        }
    }

    private void PositionCellPreview(in PreviewCell cell)
    {
        const double gap = 6;
        var cellWidth = cell.BoxCanvas.ActualWidth;
        var cellHeight = cell.BoxCanvas.ActualHeight;
        var boxWidth = cell.Box.ActualWidth;
        var boxHeight = cell.Box.ActualHeight;
        var width = cell.Control.Width;
        var height = cell.Control.Height;
        if (cellWidth <= 0 || cellHeight <= 0 || boxWidth <= 0 || boxHeight <= 0 ||
            width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height))
        {
            return;
        }

        // The mock input box stays centered; the indicator is placed with the exact
        // on-screen formula relative to the caret, overflowing the cell when needed.
        var boxX = (cellWidth - boxWidth) / 2;
        var boxY = (cellHeight - boxHeight) / 2;
        Canvas.SetLeft(cell.Box, boxX);
        Canvas.SetTop(cell.Box, boxY);
        var caretInBox = cell.Caret.TranslatePoint(new Point(), cell.Box);
        var anchor = new Rect(
            boxX + caretInBox.X,
            boxY + caretInBox.Y,
            cell.Caret.ActualWidth,
            cell.Caret.ActualHeight);
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
        Canvas.SetLeft(cell.Control, position.X + horizontalOffset);
        Canvas.SetTop(cell.Control, position.Y + verticalOffset);
    }

    private void SetPreviewCellsVisibility(Visibility visibility)
    {
        foreach (var cell in _previewCells)
        {
            cell.Control.Visibility = visibility;
        }
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
        _overlayPresenter.UpdateCustomShadow(_customShadow);
        foreach (var cell in _previewCells)
        {
            cell.Control.Configure(
                _style,
                _dotAppearance.SizeDip,
                _style == IndicatorStyle.Dot ? _lightBadgeAppearance.SizeDip : appearance.SizeDip,
                _chineseDotColor,
                _englishDotColor,
                _englishUsDotColor,
                _capsLockDotColor);
            cell.Control.UpdateCustomShadow(_customShadow);
        }

        ShowPreviewOverlay();
    }

    private static string StyleName(IndicatorStyle style) => style switch
    {
        IndicatorStyle.Dot => "圆点",
        IndicatorStyle.LightBadge => "描边",
        IndicatorStyle.ShadowBadge => "阴影",
        IndicatorStyle.Custom => "自定义",
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

    private static string FormatVersion(Version? version) => version is null
        ? "v—"
        : $"v{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private bool PersistSettings()
    {
        if (!_settingsInitialized)
        {
            return true;
        }

        var activeAppearance = GetAppearance(_style);
        return _saveSettings(new InputCueSettings(
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
            _shadowBadgeAppearance,
            _customAppearance,
            CustomIconShadow: _customShadow,
            _sameAppPromptMode,
            _sameAppPromptDelaySeconds,
            _fullScreenAutoPause));
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
