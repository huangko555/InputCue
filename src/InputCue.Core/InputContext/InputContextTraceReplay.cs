namespace InputCue.Core.InputContext;

public static class InputContextTraceReplay
{
    public static IReadOnlyList<InputContextSnapshot> Reclassify(InputContextTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (trace.SchemaVersion != InputContextTrace.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Input context trace schema {trace.SchemaVersion} is not supported.");
        }

        return trace.Observations
            .Select(Reclassify)
            .ToArray();
    }

    private static InputContextSnapshot Reclassify(InputContextDiagnostic diagnostic)
    {
        var evidence = new InputEvidence(
            diagnostic.HasEditableFocus,
            diagnostic.IsReadOnly,
            diagnostic.HasSelection,
            diagnostic.UiAutomationCaret,
            diagnostic.Win32Caret,
            diagnostic.MsaaCaret,
            diagnostic.Issue);

        return InputContextClassifier.Classify(
            diagnostic.Snapshot.Generation,
            diagnostic.Snapshot.ObservedAt,
            diagnostic.Snapshot.InputState,
            evidence);
    }
}
