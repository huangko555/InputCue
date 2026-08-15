namespace InputCue.Core.InputContext;

public static class InputContextGoldenTrace
{
    private static readonly DateTimeOffset CanonicalTimestamp = DateTimeOffset.UnixEpoch;
    private static readonly ScreenRect CanonicalRectangle = new(100, 120, 2, 20);

    public static InputContextTrace Create(IEnumerable<InputContextDiagnostic> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var canonicalObservations = observations
            .Select(Canonicalize)
            .ToArray();
        return new InputContextTrace(
            InputContextTrace.CurrentSchemaVersion,
            CanonicalTimestamp,
            canonicalObservations);
    }

    private static InputContextDiagnostic Canonicalize(
        InputContextDiagnostic diagnostic,
        int index)
    {
        var generation = checked(index + 1L);
        var observedAt = CanonicalTimestamp.AddMilliseconds(index);
        var snapshot = diagnostic.Snapshot with
        {
            Generation = generation,
            ObservedAt = observedAt,
            Anchor = Canonicalize(diagnostic.Snapshot.Anchor),
        };
        var target = diagnostic.Target with
        {
            ProcessId = 0,
        };

        return diagnostic with
        {
            Snapshot = snapshot,
            Target = target,
            UiAutomationCaret = Canonicalize(diagnostic.UiAutomationCaret),
            Win32Caret = Canonicalize(diagnostic.Win32Caret),
            MsaaCaret = Canonicalize(diagnostic.MsaaCaret),
            DurationMilliseconds = 0,
        };
    }

    private static ScreenRect? Canonicalize(ScreenRect? rectangle) =>
        rectangle is null ? null : CanonicalRectangle;
}
