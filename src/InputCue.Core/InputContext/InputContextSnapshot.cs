namespace InputCue.Core.InputContext;

public sealed record InputContextSnapshot(
    long Generation,
    DateTimeOffset ObservedAt,
    Eligibility Eligibility,
    InputState InputState,
    ScreenRect? Anchor,
    AnchorSource AnchorSource,
    EvidenceGrade EvidenceGrade,
    ReasonCode ReasonCode);
