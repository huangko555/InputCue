using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.InputContext;

public sealed class InputContextGoldenTraceTests
{
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
}
