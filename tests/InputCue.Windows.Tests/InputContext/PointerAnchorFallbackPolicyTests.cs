using InputCue.Core.Indicator;
using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class PointerAnchorFallbackPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
    private static readonly ScreenRect PointerAnchor = new(320, 240, 1, 1);

    [Fact]
    public void WpsPositionUnknownUsesRecentClickFromTheSameSurface()
    {
        var result = PointerAnchorFallbackPolicy.Apply(
            Diagnostic(),
            Click(),
            Now,
            currentForegroundWindow: 10);

        Assert.Equal(Eligibility.EditableCaret, result.Snapshot.Eligibility);
        Assert.Equal(PointerAnchor, result.Snapshot.Anchor);
        Assert.Equal(AnchorSource.PointerClick, result.Snapshot.AnchorSource);
        Assert.Equal(EvidenceGrade.Degraded, result.Snapshot.EvidenceGrade);
        Assert.Equal(ReasonCode.PointerAnchorFallback, result.Snapshot.ReasonCode);
    }

    [Fact]
    public void WpsNativeDocumentChildCanSupplyTheClickAnchor()
    {
        var result = PointerAnchorFallbackPolicy.Apply(
            Diagnostic(),
            Click(windowClassName: "_WwG"),
            Now,
            currentForegroundWindow: 10);

        Assert.Equal(AnchorSource.PointerClick, result.Snapshot.AnchorSource);
        Assert.Equal(PointerAnchor, result.Snapshot.Anchor);
    }

    [Theory]
    [InlineData(Eligibility.EditableCaret, false, false, "wps", "KxWpsView")]
    [InlineData(Eligibility.PositionUnknown, true, false, "wps", "KxWpsView")]
    [InlineData(Eligibility.PositionUnknown, false, true, "wps", "KxWpsView")]
    [InlineData(Eligibility.PositionUnknown, false, false, "other", "KxWpsView")]
    [InlineData(Eligibility.PositionUnknown, false, false, "wps", "ToolbarWindow")]
    public void ExistingEligibilityAndSafetyRulesAreNotBypassed(
        Eligibility eligibility,
        bool hasSelection,
        bool isReadOnly,
        string processName,
        string clickedClassName)
    {
        var diagnostic = Diagnostic(eligibility, hasSelection, isReadOnly, processName);

        var result = PointerAnchorFallbackPolicy.Apply(
            diagnostic,
            Click(windowClassName: clickedClassName),
            Now,
            currentForegroundWindow: 10);

        Assert.Same(diagnostic, result);
    }

    [Fact]
    public void ExpiredClickIsRejected()
    {
        var diagnostic = Diagnostic();
        var expired = Click(observedAt: Now - PointerAnchorFallbackPolicy.MaximumAge - TimeSpan.FromMilliseconds(1));

        var result = PointerAnchorFallbackPolicy.Apply(
            diagnostic,
            expired,
            Now,
            currentForegroundWindow: 10);

        Assert.Same(diagnostic, result);
    }

    [Fact]
    public void ClickSurvivesOneProbeTimeoutAndCircuitCooldown()
    {
        var diagnostic = Diagnostic();
        var recoveringClick = Click(observedAt: Now - TimeSpan.FromSeconds(3));

        var result = PointerAnchorFallbackPolicy.Apply(
            diagnostic,
            recoveringClick,
            Now,
            currentForegroundWindow: 10);

        Assert.Equal(Eligibility.EditableCaret, result.Snapshot.Eligibility);
        Assert.Equal(AnchorSource.PointerClick, result.Snapshot.AnchorSource);
    }

    [Fact]
    public void EditingKeyKeepsRecentClickWhileDelayedProbeRecovers()
    {
        var recoveringClick = Click(observedAt: Now - TimeSpan.FromSeconds(3));

        var shouldRetain = PointerAnchorFallbackPolicy.ShouldRetainAfterEditingKey(
            recoveringClick,
            Now,
            currentForegroundWindow: 10);

        Assert.True(shouldRetain);
    }

    [Fact]
    public void ClickFallbackIsNotRejectedAsStaleAfterPositionUnknownObservation()
    {
        var rawDiagnostic = Diagnostic() with
        {
            Snapshot = Diagnostic().Snapshot with
            {
                ObservedAt = Now - TimeSpan.FromMilliseconds(100),
            },
        };
        var click = Click(observedAt: Now + TimeSpan.FromMilliseconds(50));
        var session = new IndicatorSession();
        var appStayPolicy = new AppStayPromptPolicy();
        _ = appStayPolicy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never,
            TimeSpan.Zero,
            rawDiagnostic.Target,
            rawDiagnostic.Snapshot,
            Now);
        var hidden = session.Observe(rawDiagnostic.Snapshot, receivedAt: Now);

        var effectiveDiagnostic = PointerAnchorFallbackPolicy.Apply(
            rawDiagnostic,
            click,
            Now + TimeSpan.FromMilliseconds(50),
            currentForegroundWindow: 10);
        var suppress = appStayPolicy.ShouldSuppressContextReplay(
            AppStayPromptMode.Never,
            TimeSpan.Zero,
            effectiveDiagnostic.Target,
            effectiveDiagnostic.Snapshot,
            Now + TimeSpan.FromMilliseconds(50));
        var visible = session.Observe(
            effectiveDiagnostic.Snapshot,
            receivedAt: Now + TimeSpan.FromMilliseconds(50),
            suppressContextReplay: suppress);

        Assert.False(hidden.IsVisible);
        Assert.False(suppress);
        Assert.True(visible.IsVisible);
        Assert.Equal(IndicatorReasonCode.ContextEstablished, visible.ReasonCode);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(6, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 11)]
    public void EditingKeyRejectsClickOutsideItsRecoveryWindow(
        int clickAgeSeconds,
        long foregroundWindow)
    {
        var click = Click(observedAt: Now - TimeSpan.FromSeconds(clickAgeSeconds));

        var shouldRetain = PointerAnchorFallbackPolicy.ShouldRetainAfterEditingKey(
            click,
            Now,
            currentForegroundWindow: (nint)foregroundWindow);

        Assert.False(shouldRetain);
    }

    [Theory]
    [InlineData(11, 20)]
    [InlineData(10, 21)]
    public void WindowAndProcessMismatchAreRejected(long foregroundWindow, int processId)
    {
        var diagnostic = Diagnostic();

        var result = PointerAnchorFallbackPolicy.Apply(
            diagnostic,
            Click(foregroundWindow: (nint)foregroundWindow, processId: processId),
            Now,
            currentForegroundWindow: 10);

        Assert.Same(diagnostic, result);
    }

    private static InputContextDiagnostic Diagnostic(
        Eligibility eligibility = Eligibility.PositionUnknown,
        bool hasSelection = false,
        bool isReadOnly = false,
        string processName = "wps") =>
        new(
            new InputContextSnapshot(
                1,
                Now,
                eligibility,
                InputState.Chinese,
                eligibility is Eligibility.EditableCaret ? new ScreenRect(10, 10, 1, 20) : null,
                eligibility is Eligibility.EditableCaret ? AnchorSource.UiAutomation : AnchorSource.None,
                EvidenceGrade.Confirmed,
                eligibility is Eligibility.EditableCaret
                    ? ReasonCode.EditableCaretConfirmed
                    : ReasonCode.PositionUnavailable),
            new TargetDescriptor(20, processName, "ControlType.Group", "KxWpsView", "Qt"),
            HasEditableFocus: true,
            IsReadOnly: isReadOnly,
            HasSelection: hasSelection,
            UiAutomationCaret: null,
            UiAutomationCaretMethod.None,
            TextPattern2Status.NotAttempted,
            Win32Caret: null,
            MsaaCaret: null,
            ProbeIssue.None,
            DurationMilliseconds: 1);

    private static PointerClickObservation Click(
        DateTimeOffset? observedAt = null,
        nint? foregroundWindow = null,
        int processId = 20,
        string windowClassName = "KxWpsView") =>
        new(
            observedAt ?? Now - TimeSpan.FromMilliseconds(100),
            foregroundWindow ?? 10,
            processId,
            windowClassName,
            PointerAnchor);
}
