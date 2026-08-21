using InputCue.Core.Indicator;
using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.Indicator;

public sealed class AppStayPromptPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameApplicationConsumesOnlyItsFirstPromptOpportunity()
    {
        var policy = new AppStayPromptPolicy();

        Assert.False(policy.ShouldSuppressContextReplay(Target("editor", 10), Snapshot(1)));
        Assert.True(policy.ShouldSuppressContextReplay(Target("EDITOR", 11), Snapshot(2)));
    }

    [Fact]
    public void SwitchingAwayAndBackStartsANewStay()
    {
        var policy = new AppStayPromptPolicy();
        _ = policy.ShouldSuppressContextReplay(Target("editor", 10), Snapshot(1));

        Assert.False(policy.ShouldSuppressContextReplay(Target("browser", 20), Snapshot(2)));
        Assert.False(policy.ShouldSuppressContextReplay(Target("editor", 10), Snapshot(3)));
    }

    [Fact]
    public void IneligibleObservationDoesNotConsumePromptOpportunity()
    {
        var policy = new AppStayPromptPolicy();

        Assert.False(policy.ShouldSuppressContextReplay(
            Target("editor", 10),
            Snapshot(1, Eligibility.PositionUnknown, anchor: null)));
        Assert.False(policy.ShouldSuppressContextReplay(Target("editor", 10), Snapshot(2)));
    }

    [Fact]
    public void ResetStartsANewStayWithoutPolling()
    {
        var policy = new AppStayPromptPolicy();
        _ = policy.ShouldSuppressContextReplay(Target("editor", 10), Snapshot(1));

        policy.Reset();

        Assert.False(policy.ShouldSuppressContextReplay(Target("editor", 10), Snapshot(2)));
    }

    private static TargetDescriptor Target(string processName, int processId) =>
        new(processId, processName, "ControlType.Edit", "Edit", "Test");

    private static InputContextSnapshot Snapshot(
        long generation,
        Eligibility eligibility = Eligibility.EditableCaret,
        ScreenRect? anchor = null) =>
        new(
            Generation: generation,
            ObservedAt: Now.AddMilliseconds(generation),
            Eligibility: eligibility,
            InputState: InputState.Chinese,
            Anchor: anchor ?? new ScreenRect(100, 100, 2, 20),
            AnchorSource: AnchorSource.UiAutomation,
            EvidenceGrade: EvidenceGrade.Confirmed,
            ReasonCode: ReasonCode.EditableCaretConfirmed);
}
