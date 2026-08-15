namespace InputCue.Core.InputContext;

public sealed record InputContextTrace(
    int SchemaVersion,
    DateTimeOffset ExportedAt,
    IReadOnlyList<InputContextDiagnostic> Observations)
{
    public const int CurrentSchemaVersion = 1;

    public static InputContextTrace Create(IEnumerable<InputContextDiagnostic> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return new InputContextTrace(
            CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            observations.ToArray());
    }
}
