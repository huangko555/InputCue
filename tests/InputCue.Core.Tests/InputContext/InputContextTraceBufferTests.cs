using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.InputContext;

public sealed class InputContextTraceBufferTests
{
    [Fact]
    public void AddCollapsesConsecutiveSemanticDuplicatesAndKeepsTheLatestSample()
    {
        var buffer = new InputContextTraceBuffer(10);
        var first = Diagnostic(DateTimeOffset.UnixEpoch, 10);
        var latest = Diagnostic(DateTimeOffset.UnixEpoch.AddSeconds(1), 20);

        buffer.Add(first);
        buffer.Add(latest);

        Assert.Collection(buffer.Observations, observation => Assert.Same(latest, observation));
    }

    [Fact]
    public void AddRetainsAnImeEvidenceChangeWithinTheSameGeneration()
    {
        var buffer = new InputContextTraceBuffer(10);
        var chineseFacts = Diagnostic(DateTimeOffset.UnixEpoch, 10) with
        {
            InputStateEvidence = new InputStateEvidence(
                0x0804,
                true,
                false,
                null,
                null,
                true,
                1,
                1),
        };
        var englishFacts = chineseFacts with
        {
            Snapshot = chineseFacts.Snapshot with
            {
                ObservedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
            },
            InputStateEvidence = chineseFacts.InputStateEvidence! with
            {
                ImeWindowConversionMode = 0,
            },
        };

        buffer.Add(chineseFacts);
        buffer.Add(englishFacts);

        Assert.Equal(2, buffer.Observations.Count);
    }

    [Fact]
    public void AddDropsTheOldestDistinctObservationAtCapacity()
    {
        var buffer = new InputContextTraceBuffer(2);

        buffer.Add(Diagnostic(DateTimeOffset.UnixEpoch, 10));
        buffer.Add(Diagnostic(DateTimeOffset.UnixEpoch.AddSeconds(1), 10, generation: 2));
        buffer.Add(Diagnostic(DateTimeOffset.UnixEpoch.AddSeconds(2), 10, generation: 3));

        Assert.Equal([2L, 3L], buffer.Observations.Select(item => item.Snapshot.Generation));
    }

    private static InputContextDiagnostic Diagnostic(
        DateTimeOffset observedAt,
        double durationMilliseconds,
        long generation = 1)
    {
        var anchor = new ScreenRect(100, 120, 2, 20);
        var snapshot = new InputContextSnapshot(
            generation,
            observedAt,
            Eligibility.EditableCaret,
            InputState.Unknown,
            anchor,
            AnchorSource.Win32,
            EvidenceGrade.Degraded,
            ReasonCode.EditableCaretConfirmed);
        return new InputContextDiagnostic(
            snapshot,
            new TargetDescriptor(7, "target", "ControlType.Edit", "Edit", "Test"),
            true,
            false,
            false,
            null,
            UiAutomationCaretMethod.None,
            TextPattern2Status.PatternUnavailable,
            anchor,
            null,
            ProbeIssue.None,
            durationMilliseconds,
            InputStateEvidence.Unavailable);
    }
}
