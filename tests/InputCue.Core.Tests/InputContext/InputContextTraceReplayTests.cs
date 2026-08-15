using System.Text.Json;
using System.Text.Json.Serialization;
using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.InputContext;

public sealed class InputContextTraceReplayTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void ExportedTraceRoundTripsAndReclassifiesWithoutSemanticDrift()
    {
        var observedAt = new DateTimeOffset(2026, 8, 16, 1, 2, 3, TimeSpan.Zero);
        var caret = new ScreenRect(10, 20, 2, 18);
        var evidence = new InputEvidence(true, false, false, caret, null, null);
        var snapshot = InputContextClassifier.Classify(7, observedAt, InputState.Unknown, evidence);
        var diagnostic = new InputContextDiagnostic(
            snapshot,
            new TargetDescriptor(42, "sample", "ControlType.Edit", "Edit", "Win32"),
            true,
            false,
            false,
            caret,
            UiAutomationCaretMethod.TextPattern2,
            TextPattern2Status.Succeeded,
            null,
            null,
            ProbeIssue.None,
            3.5);
        var trace = InputContextTrace.Create([diagnostic]);

        var json = JsonSerializer.Serialize(trace, JsonOptions);
        var restored = JsonSerializer.Deserialize<InputContextTrace>(json, JsonOptions);
        var replayed = InputContextTraceReplay.Reclassify(Assert.IsType<InputContextTrace>(restored));

        var replayedSnapshot = Assert.Single(replayed);
        Assert.Equal(snapshot, replayedSnapshot);
    }

    [Fact]
    public void ReclassifyRejectsUnknownSchema()
    {
        var trace = new InputContextTrace(999, DateTimeOffset.UtcNow, []);

        _ = Assert.Throws<NotSupportedException>(() => InputContextTraceReplay.Reclassify(trace));
    }
}
