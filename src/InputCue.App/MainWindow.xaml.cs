using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;
using Microsoft.Win32;

namespace InputCue.App;

public partial class MainWindow : Window, IDisposable
{
    private const int HistoryCapacity = 200;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    private readonly InputContextEngine _engine = new();
    private readonly List<InputContextDiagnostic> _history = [];
    private CancellationTokenSource? _watchCancellation;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => StartWatching();

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
        GC.SuppressFinalize(this);
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_watchCancellation is null)
        {
            StartWatching();
            return;
        }

        StopWatching();
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0)
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
            var trace = InputContextTrace.Create(_history);
            var json = JsonSerializer.Serialize(trace, JsonOptions);
            await File.WriteAllTextAsync(dialog.FileName, json, Encoding.UTF8);
            StatusText.Text = $"已导出 {_history.Count} 条脱敏观察。";
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
        _ = WatchAsync(_watchCancellation.Token);
    }

    private void StopWatching()
    {
        var cancellation = _watchCancellation;
        _watchCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        PauseButton.Content = "继续";
        StatusText.Text = "诊断探针已暂停。";
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
            });
        }
    }

    private void ShowDiagnostic(InputContextDiagnostic diagnostic)
    {
        _history.Add(diagnostic);
        if (_history.Count > HistoryCapacity)
        {
            _history.RemoveAt(0);
        }

        StatusText.Text =
            $"正在监听 · 最近观察 {diagnostic.Snapshot.ObservedAt.ToLocalTime():HH:mm:ss.fff} · " +
            $"耗时 {diagnostic.DurationMilliseconds:F1} ms";
        DiagnosticText.Text = FormatDiagnostic(diagnostic);
    }

    private static string FormatDiagnostic(InputContextDiagnostic diagnostic)
    {
        var snapshot = diagnostic.Snapshot;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Generation        {snapshot.Generation}
            Eligibility       {snapshot.Eligibility}
            InputState        {snapshot.InputState}
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
}
