using System.Text.Json;
using System.Text.Json.Serialization;
using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.InputContext;

public sealed class InputContextGoldenTraceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void CreateRemovesMachineSpecificValuesWithoutChangingSemantics()
    {
        var observedAt = new DateTimeOffset(2026, 8, 16, 12, 30, 0, TimeSpan.Zero);
        var caret = new ScreenRect(1920, 1080, 3, 28);
        var evidence = new InputEvidence(true, false, false, caret, null, null);
        var snapshot = InputContextClassifier.Classify(42, observedAt, InputState.Unknown, evidence);
        var diagnostic = new InputContextDiagnostic(
            snapshot,
            new TargetDescriptor(1234, "sample", "ControlType.Edit", "Edit", "WPF"),
            true,
            false,
            false,
            caret,
            UiAutomationCaretMethod.TextPattern,
            TextPattern2Status.PatternUnavailable,
            null,
            null,
            ProbeIssue.None,
            87.4);

        var trace = InputContextGoldenTrace.Create([diagnostic]);
        var canonical = Assert.Single(trace.Observations);
        var replayed = Assert.Single(InputContextTraceReplay.Reclassify(trace));

        Assert.Equal(DateTimeOffset.UnixEpoch, trace.ExportedAt);
        Assert.Equal(0, canonical.Target.ProcessId);
        Assert.Equal(1, canonical.Snapshot.Generation);
        Assert.Equal(DateTimeOffset.UnixEpoch, canonical.Snapshot.ObservedAt);
        Assert.Equal(0, canonical.DurationMilliseconds);
        Assert.NotEqual(caret, canonical.UiAutomationCaret);
        Assert.Equal(snapshot.Eligibility, replayed.Eligibility);
        Assert.Equal(snapshot.AnchorSource, replayed.AnchorSource);
        Assert.Equal(snapshot.ReasonCode, replayed.ReasonCode);
    }

    [Fact]
    public void CreatePreservesGenerationEquivalenceWhileRenumbering()
    {
        var first = Diagnostic(generation: 42);
        var repeated = Diagnostic(generation: 42);
        var changed = Diagnostic(generation: 99);

        var trace = InputContextGoldenTrace.Create([first, repeated, changed]);

        Assert.Equal(
            [1L, 1L, 2L],
            trace.Observations.Select(observation => observation.Snapshot.Generation));
    }

    [Theory]
    [InlineData("wpf-native-basic.json")]
    [InlineData("edge-basic.json")]
    [InlineData("chrome-basic.json")]
    [InlineData("system-dialog-basic.json")]
    public void GoldenTraceReplaysWithoutSemanticDrift(string fileName)
    {
        var trace = ReadTrace(fileName);

        var replayed = InputContextTraceReplay.Reclassify(trace);

        Assert.Equal(trace.Observations.Count, replayed.Count);
        for (var index = 0; index < replayed.Count; index++)
        {
            Assert.Equal(trace.Observations[index].Snapshot.Eligibility, replayed[index].Eligibility);
            Assert.Equal(trace.Observations[index].Snapshot.AnchorSource, replayed[index].AnchorSource);
            Assert.Equal(trace.Observations[index].Snapshot.ReasonCode, replayed[index].ReasonCode);
        }
    }

    [Fact]
    public void SystemDialogTraceKeepsHoverStableAndRejectsNonTextControls()
    {
        var observations = ReadTrace("system-dialog-basic.json").Observations;

        Assert.Collection(
            observations,
            observation => Assert.Equal(Eligibility.EditableCaret, observation.Snapshot.Eligibility),
            observation => Assert.Equal(Eligibility.EditableCaret, observation.Snapshot.Eligibility),
            observation =>
            {
                Assert.Equal("ControlType.ComboBox", observation.Target.ControlType);
                Assert.Equal(Eligibility.NoEditableFocus, observation.Snapshot.Eligibility);
            },
            observation =>
            {
                Assert.Equal("ControlType.Button", observation.Target.ControlType);
                Assert.Equal(Eligibility.NoEditableFocus, observation.Snapshot.Eligibility);
            });
        Assert.Equal(observations[0].Snapshot.Generation, observations[1].Snapshot.Generation);
        Assert.Equal(observations[0].Target, observations[1].Target);
    }

    private static InputContextTrace ReadTrace(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "traces", fileName);
        var json = File.ReadAllText(path);
        return Assert.IsType<InputContextTrace>(
            JsonSerializer.Deserialize<InputContextTrace>(json, JsonOptions));
    }

    private static InputContextDiagnostic Diagnostic(long generation)
    {
        var evidence = new InputEvidence(false, null, null, null, null, null);
        var snapshot = InputContextClassifier.Classify(
            generation,
            DateTimeOffset.UtcNow,
            InputState.Unknown,
            evidence);
        return new InputContextDiagnostic(
            snapshot,
            new TargetDescriptor(1, "sample", "ControlType.Button", "Button", "Win32"),
            false,
            null,
            null,
            null,
            UiAutomationCaretMethod.None,
            TextPattern2Status.NotAttempted,
            null,
            null,
            ProbeIssue.None,
            1);
    }
}
