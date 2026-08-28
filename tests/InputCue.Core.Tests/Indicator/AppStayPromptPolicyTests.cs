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

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never, TimeSpan.Zero, Target("editor", 10), Snapshot(1), Now));
        Assert.True(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never, TimeSpan.Zero, Target("EDITOR", 11), Snapshot(2), Now));
    }

    [Fact]
    public void SwitchingAwayAndBackStartsANewStay()
    {
        var policy = new AppStayPromptPolicy();
        _ = policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never, TimeSpan.Zero, Target("editor", 10), Snapshot(1), Now);

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never, TimeSpan.Zero, Target("browser", 20), Snapshot(2), Now));
        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never, TimeSpan.Zero, Target("editor", 10), Snapshot(3), Now));
    }

    [Fact]
    public void IneligibleObservationDoesNotConsumePromptOpportunity()
    {
        var policy = new AppStayPromptPolicy();

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never,
            TimeSpan.Zero,
            Target("editor", 10),
            Snapshot(1, Eligibility.PositionUnknown, anchor: null),
            Now));
        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never, TimeSpan.Zero, Target("editor", 10), Snapshot(2), Now));
    }

    [Fact]
    public void ResetStartsANewStayWithoutPolling()
    {
        var policy = new AppStayPromptPolicy();
        _ = policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never, TimeSpan.Zero, Target("editor", 10), Snapshot(1), Now);

        policy.Reset();

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never, TimeSpan.Zero, Target("editor", 10), Snapshot(2), Now));
    }

    [Fact]
    public void AlwaysModeNeverSuppressesReplays()
    {
        var policy = new AppStayPromptPolicy();

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Always, TimeSpan.Zero, Target("editor", 10), Snapshot(1), Now));
        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Always, TimeSpan.Zero, Target("editor", 10), Snapshot(2), Now));
        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.Always, TimeSpan.Zero, Target("editor", 10), Snapshot(3), Now));
    }

    [Fact]
    public void AfterDelaySuppressesUntilDelayElapsedSinceLastPrompt()
    {
        var policy = new AppStayPromptPolicy();

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(1),
            Now));
        Assert.True(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(2),
            Now.AddSeconds(299)));
        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(3),
            Now.AddSeconds(300)));
    }

    [Fact]
    public void AfterDelayRestartsItsTimerWhenReplayIsAllowed()
    {
        var policy = new AppStayPromptPolicy();

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(1),
            Now));
        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(2),
            Now.AddMinutes(31)));
        Assert.True(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(3),
            Now.AddMinutes(35)));
        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(4),
            Now.AddMinutes(40)));
    }

    [Fact]
    public void AfterDelayStartsANewStayWhenApplicationChanges()
    {
        var policy = new AppStayPromptPolicy();
        _ = policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(1),
            Now);
        _ = policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("browser", 20),
            Snapshot(2),
            Now.AddMinutes(1));

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(3),
            Now.AddMinutes(2)));
        Assert.True(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(4),
            Now.AddMinutes(2).AddSeconds(30)));
    }

    [Fact]
    public void AfterDelayIgnoresIneligibleObservationsForItsTimer()
    {
        var policy = new AppStayPromptPolicy();
        _ = policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(1),
            Now);

        Assert.False(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(2, Eligibility.PositionUnknown, anchor: null),
            Now.AddSeconds(150)));
        Assert.True(policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(300),
            Target("editor", 10),
            Snapshot(3),
            Now.AddSeconds(200)));
    }

    [Fact]
    public void NegativeDelayIsRejected()
    {
        var policy = new AppStayPromptPolicy();

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.ShouldSuppressContextReplay(
            AppStayPromptMode.AfterDelay,
            TimeSpan.FromSeconds(-1),
            Target("editor", 10),
            Snapshot(1),
            Now));
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
