namespace InputCue.Core.InputContext;

public sealed record InputStateEvidence(
    ushort? LanguageId,
    bool? IsIme,
    bool? HasImeContext,
    bool? ImeOpen,
    uint? ConversionMode)
{
    public static readonly InputStateEvidence Unavailable = new(
        null,
        null,
        null,
        null,
        null);
}
