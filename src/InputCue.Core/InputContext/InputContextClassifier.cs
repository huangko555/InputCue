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
            if (evidence.Win32Caret is { IsUsable: true } fallbackWin32Caret &&
                IsContainerLike(uiAutomationCaret, fallbackWin32Caret))
            {
                return (fallbackWin32Caret, AnchorSource.Win32, EvidenceGrade.Degraded);
            }

            if (evidence.MsaaCaret is { IsUsable: true } fallbackMsaaCaret &&
                IsContainerLike(uiAutomationCaret, fallbackMsaaCaret))
            {
                return (fallbackMsaaCaret, AnchorSource.Msaa, EvidenceGrade.Degraded);
            }

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

    private static bool IsContainerLike(ScreenRect candidate, ScreenRect fallback)
    {
        const double coordinateTolerance = 2;

        // Some TextPattern providers expose the whole focused control as a collapsed range.
        // Downgrade only when an independent caret is inside and substantially smaller.
        var containsFallback = candidate.X - coordinateTolerance <= fallback.X &&
            candidate.Y - coordinateTolerance <= fallback.Y &&
            candidate.X + candidate.Width + coordinateTolerance >= fallback.X + fallback.Width &&
            candidate.Y + candidate.Height + coordinateTolerance >= fallback.Y + fallback.Height;
        if (!containsFallback)
        {
            return false;
        }

        return candidate.Width >= Math.Max(32, fallback.Width * 8) ||
            candidate.Height >= Math.Max(48, fallback.Height * 2);
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
