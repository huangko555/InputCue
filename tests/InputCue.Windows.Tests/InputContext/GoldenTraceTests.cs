using System.Text.Json;
using System.Text.Json.Serialization;
using InputCue.Core.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class GoldenTraceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void NativeWpfTracePreservesPositiveAndNegativeScenarios()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "traces", "wpf-native-basic.json");
        var trace = JsonSerializer.Deserialize<InputContextTrace>(File.ReadAllText(path), JsonOptions);

        var replayed = InputContextTraceReplay.Reclassify(Assert.IsType<InputContextTrace>(trace));

        Assert.Equal(DateTimeOffset.UnixEpoch, trace.ExportedAt);
        Assert.All(trace.Observations, diagnostic =>
        {
            Assert.Equal(0, diagnostic.Target.ProcessId);
            Assert.Equal(0, diagnostic.DurationMilliseconds);
        });
        Assert.Collection(
            replayed,
            snapshot =>
            {
                Assert.Equal(Eligibility.EditableCaret, snapshot.Eligibility);
                Assert.NotNull(snapshot.Anchor);
            },
            snapshot => Assert.Equal(Eligibility.EditableSelection, snapshot.Eligibility),
            snapshot => Assert.Equal(Eligibility.ReadOnlySelection, snapshot.Eligibility),
            snapshot => Assert.Equal(Eligibility.NoEditableFocus, snapshot.Eligibility));
        Assert.All(replayed.Skip(1), snapshot => Assert.Null(snapshot.Anchor));
    }

    [Theory]
    [InlineData("edge-basic.json", "msedge")]
    [InlineData("chrome-basic.json", "chrome")]
    public void ChromiumTraceDistinguishesTextEditingFromDocumentSelectionAndRadioButton(
        string fileName,
        string processName)
    {
        var trace = ReadTrace(fileName);

        var replayed = InputContextTraceReplay.Reclassify(trace);

        Assert.Collection(
            replayed,
            snapshot => Assert.Equal(Eligibility.EditableCaret, snapshot.Eligibility),
            snapshot => Assert.Equal(Eligibility.EditableSelection, snapshot.Eligibility),
            snapshot => Assert.Equal(Eligibility.ReadOnlySelection, snapshot.Eligibility),
            snapshot => Assert.Equal(Eligibility.NoEditableFocus, snapshot.Eligibility));
        Assert.Equal("ControlType.RadioButton", trace.Observations[3].Target.ControlType);
        Assert.False(trace.Observations[3].HasEditableFocus);
        Assert.All(trace.Observations, observation =>
            Assert.Equal(processName, observation.Target.ProcessName));
    }

    [Fact]
    public void RapidFocusSwitchTraceFinishesOnTheLatestTarget()
    {
        var trace = ReadTrace("rapid-focus-switch-basic.json");

        var replayed = InputContextTraceReplay.Reclassify(trace);

        Assert.Equal(
            ["chrome", "Notepad", "chrome", "Notepad", "chrome", "chrome"],
            trace.Observations.Select(observation => observation.Target.ProcessName));
        Assert.Equal(
            [1L, 2L, 3L, 4L, 5L, 5L],
            trace.Observations.Select(observation => observation.Snapshot.Generation));
        Assert.All(trace.Observations, observation => Assert.Equal(ProbeIssue.None, observation.Issue));
        Assert.All(replayed, snapshot => Assert.Equal(Eligibility.EditableCaret, snapshot.Eligibility));
        Assert.Equal(trace.Observations[^2].Target, trace.Observations[^1].Target);
    }

    [Fact]
    public void WebView2ProseMirrorTracePreservesEditableCaret()
    {
        var trace = ReadTrace("webview2-prosemirror-basic.json");

        var snapshot = Assert.Single(InputContextTraceReplay.Reclassify(trace));

        Assert.Equal(Eligibility.EditableCaret, snapshot.Eligibility);
        Assert.Equal(AnchorSource.UiAutomation, snapshot.AnchorSource);
        Assert.NotNull(snapshot.Anchor);
        Assert.Equal("msedgewebview2", Assert.Single(trace.Observations).Target.ProcessName);
    }

    [Fact]
    public void FeishuWebTraceAcceptsDocumentEditingAndRejectsNearbySurfaces()
    {
        var trace = ReadTrace("feishu-web-basic.json");

        var replayed = InputContextTraceReplay.Reclassify(trace);

        Assert.Collection(
            replayed,
            snapshot => Assert.Equal(Eligibility.EditableCaret, snapshot.Eligibility),
            snapshot => Assert.Equal(Eligibility.EditableSelection, snapshot.Eligibility),
            snapshot => Assert.Equal(Eligibility.NoEditableFocus, snapshot.Eligibility),
            snapshot => Assert.Equal(Eligibility.ReadOnlySelection, snapshot.Eligibility));
        Assert.Equal("page-block root-block", trace.Observations[0].Target.ClassName);
        Assert.Equal("docx-selection-hidden-textarea", trace.Observations[3].Target.ClassName);
        Assert.False(trace.Observations[3].HasEditableFocus);
    }

    private static InputContextTrace ReadTrace(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "traces", fileName);
        var trace = JsonSerializer.Deserialize<InputContextTrace>(File.ReadAllText(path), JsonOptions);
        return Assert.IsType<InputContextTrace>(trace);
    }
}
