using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.InputContext;

public sealed class InputContextClassifierTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly ScreenRect Caret = new(100, 120, 2, 20);

    [Fact]
    public void ClassifyShowsConfirmedEditableCaret()
    {
        var evidence = Evidence(editable: true, readOnly: false, selection: false, uiAutomationCaret: Caret);

        var result = InputContextClassifier.Classify(1, ObservedAt, InputState.Unknown, evidence);

        Assert.Equal(Eligibility.EditableCaret, result.Eligibility);
        Assert.Equal(AnchorSource.UiAutomation, result.AnchorSource);
        Assert.Equal(EvidenceGrade.Confirmed, result.EvidenceGrade);
    }

    [Fact]
    public void ClassifyHidesEditableSelection()
    {
        var evidence = Evidence(editable: true, readOnly: false, selection: true, uiAutomationCaret: Caret);

        var result = InputContextClassifier.Classify(1, ObservedAt, InputState.Unknown, evidence);

        Assert.Equal(Eligibility.EditableSelection, result.Eligibility);
        Assert.Null(result.Anchor);
    }

    [Fact]
    public void ClassifyHidesReadOnlySelection()
    {
        var evidence = Evidence(editable: false, readOnly: true, selection: true, uiAutomationCaret: Caret);

        var result = InputContextClassifier.Classify(1, ObservedAt, InputState.Unknown, evidence);

        Assert.Equal(Eligibility.ReadOnlySelection, result.Eligibility);
        Assert.Null(result.Anchor);
    }

    [Fact]
    public void ClassifyDoesNotUseCaretWithoutEditableEvidence()
    {
        var evidence = Evidence(editable: false, readOnly: null, selection: false, uiAutomationCaret: Caret);

        var result = InputContextClassifier.Classify(1, ObservedAt, InputState.Unknown, evidence);

        Assert.Equal(Eligibility.NoEditableFocus, result.Eligibility);
        Assert.Null(result.Anchor);
    }

    [Fact]
    public void ClassifyUsesWin32CaretOnlyAsDegradedAnchor()
    {
        var evidence = Evidence(editable: true, readOnly: false, selection: false, win32Caret: Caret);

        var result = InputContextClassifier.Classify(1, ObservedAt, InputState.Unknown, evidence);

        Assert.Equal(Eligibility.EditableCaret, result.Eligibility);
        Assert.Equal(AnchorSource.Win32, result.AnchorSource);
        Assert.Equal(EvidenceGrade.Degraded, result.EvidenceGrade);
    }

    [Theory]
    [InlineData(ProbeIssue.TimedOut, ReasonCode.TimedOut)]
    [InlineData(ProbeIssue.InsufficientPrivilege, ReasonCode.InsufficientPrivilege)]
    [InlineData(ProbeIssue.ConflictingEvidence, ReasonCode.ConflictingEvidence)]
    [InlineData(ProbeIssue.SourceUnavailable, ReasonCode.SourceUnavailable)]
    public void ClassifyHidesProbeFailures(ProbeIssue issue, ReasonCode reasonCode)
    {
        var evidence = Evidence(editable: true, readOnly: false, selection: false, uiAutomationCaret: Caret) with
        {
            Issue = issue,
        };

        var result = InputContextClassifier.Classify(1, ObservedAt, InputState.Unknown, evidence);

        Assert.Equal(Eligibility.Unknown, result.Eligibility);
        Assert.Equal(reasonCode, result.ReasonCode);
        Assert.Null(result.Anchor);
    }

    private static InputEvidence Evidence(
        bool editable,
        bool? readOnly,
        bool? selection,
        ScreenRect? uiAutomationCaret = null,
        ScreenRect? win32Caret = null) =>
        new(
            editable,
            readOnly,
            selection,
            uiAutomationCaret,
            win32Caret,
            null);
}
