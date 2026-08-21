using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal static class PointerAnchorFallbackPolicy
{
    // One UIA timeout can be followed by the engine's two-second circuit cooldown.
    // Keep the validated click long enough for the next healthy observation to use it.
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(5);

    internal static InputContextDiagnostic Apply(
        InputContextDiagnostic diagnostic,
        PointerClickObservation? click,
        DateTimeOffset now,
        nint currentForegroundWindow)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (click is null ||
            now < click.ObservedAt ||
            now - click.ObservedAt > MaximumAge ||
            currentForegroundWindow == 0 ||
            click.ForegroundWindow != currentForegroundWindow ||
            click.ProcessId != diagnostic.Target.ProcessId ||
            !AppProfileCatalog.SupportsPointerAnchorFallback(
                diagnostic.Target,
                click.WindowClassName) ||
            diagnostic.Snapshot.Eligibility is not Eligibility.PositionUnknown ||
            diagnostic.Snapshot.InputState is InputState.Unknown ||
            !diagnostic.HasEditableFocus ||
            diagnostic.IsReadOnly is not false ||
            diagnostic.HasSelection is true ||
            diagnostic.Issue is not ProbeIssue.None ||
            !click.Anchor.IsUsable)
        {
            return diagnostic;
        }

        return diagnostic with
        {
            Snapshot = diagnostic.Snapshot with
            {
                ObservedAt = diagnostic.Snapshot.ObservedAt >= click.ObservedAt
                    ? diagnostic.Snapshot.ObservedAt
                    : click.ObservedAt,
                Eligibility = Eligibility.EditableCaret,
                Anchor = click.Anchor,
                AnchorSource = AnchorSource.PointerClick,
                EvidenceGrade = EvidenceGrade.Degraded,
                ReasonCode = ReasonCode.PointerAnchorFallback,
            },
        };
    }

    internal static bool ShouldRetainAfterEditingKey(
        PointerClickObservation? click,
        DateTimeOffset now,
        nint currentForegroundWindow) =>
        click is not null &&
        now >= click.ObservedAt &&
        now - click.ObservedAt <= MaximumAge &&
        currentForegroundWindow != 0 &&
        click.ForegroundWindow == currentForegroundWindow;
}
