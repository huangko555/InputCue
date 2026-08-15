namespace InputCue.Core.InputContext;

public sealed record InputStateEvidence(
    ushort? LanguageId,
    bool? IsIme,
    bool? HasImeContext,
    bool? ImeOpen,
    uint? ConversionMode,
    bool? HasDefaultImeWindow = null,
    uint? ImeWindowOpenStatus = null,
    uint? ImeWindowConversionMode = null)
{
    public static readonly InputStateEvidence Unavailable = new(
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);
}
