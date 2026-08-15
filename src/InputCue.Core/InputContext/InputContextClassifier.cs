namespace InputCue.Core.InputContext;

public static class InputContextClassifier
{
    public static InputContextSnapshot Classify(
        long generation,
        DateTimeOffset observedAt,
        InputState inputState,
        InputEvidence evidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Issue is not ProbeIssue.None)
        {
            return Hidden(generation, observedAt, inputState, ReasonFor(evidence.Issue));
        }

        if (evidence.HasSelection is true)
        {
            var eligibility = evidence.HasEditableFocus && evidence.IsReadOnly is false
                ? Eligibility.EditableSelection
                : Eligibility.ReadOnlySelection;
            var reason = eligibility is Eligibility.EditableSelection
                ? ReasonCode.EditableSelection
                : ReasonCode.ReadOnlySelection;

            return new InputContextSnapshot(
                generation,
                observedAt,
                eligibility,
                inputState,
                null,
                AnchorSource.None,
                EvidenceGrade.Confirmed,
                reason);
        }

        if (!evidence.HasEditableFocus || evidence.IsReadOnly is not false)
        {
            return new InputContextSnapshot(
                generation,
                observedAt,
                Eligibility.NoEditableFocus,
                inputState,
                null,
                AnchorSource.None,
                EvidenceGrade.Confirmed,
                ReasonCode.NoEditableFocus);
        }

        var (anchor, source, grade) = SelectAnchor(evidence);
        if (anchor is null)
        {
            return new InputContextSnapshot(
                generation,
                observedAt,
                Eligibility.PositionUnknown,
                inputState,
                null,
                AnchorSource.None,
                EvidenceGrade.Confirmed,
                ReasonCode.PositionUnavailable);
        }

        return new InputContextSnapshot(
            generation,
            observedAt,
            Eligibility.EditableCaret,
            inputState,
            anchor,
            source,
            grade,
            ReasonCode.EditableCaretConfirmed);
    }

    private static InputContextSnapshot Hidden(
        long generation,
        DateTimeOffset observedAt,
        InputState inputState,
        ReasonCode reasonCode) =>
        new(
            generation,
            observedAt,
            Eligibility.Unknown,
            inputState,
            null,
            AnchorSource.None,
            EvidenceGrade.Unknown,
            reasonCode);

    private static (ScreenRect? Anchor, AnchorSource Source, EvidenceGrade Grade) SelectAnchor(
        InputEvidence evidence)
    {
        if (evidence.UiAutomationCaret is { IsUsable: true } uiAutomationCaret)
        {
            return (uiAutomationCaret, AnchorSource.UiAutomation, EvidenceGrade.Confirmed);
        }

        if (evidence.Win32Caret is { IsUsable: true } win32Caret)
        {
            return (win32Caret, AnchorSource.Win32, EvidenceGrade.Degraded);
        }

        if (evidence.MsaaCaret is { IsUsable: true } msaaCaret)
        {
            return (msaaCaret, AnchorSource.Msaa, EvidenceGrade.Degraded);
        }

        return (null, AnchorSource.None, EvidenceGrade.Unknown);
    }

    private static ReasonCode ReasonFor(ProbeIssue issue) => issue switch
    {
        ProbeIssue.TimedOut => ReasonCode.TimedOut,
        ProbeIssue.InsufficientPrivilege => ReasonCode.InsufficientPrivilege,
        ProbeIssue.ConflictingEvidence => ReasonCode.ConflictingEvidence,
        ProbeIssue.SourceUnavailable => ReasonCode.SourceUnavailable,
        _ => ReasonCode.None,
    };
}
