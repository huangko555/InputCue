using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class InputTargetContinuityTests
{
    [Fact]
    public void RuntimeIdChangeInsideTheSameEditorKeepsTheTargetContinuous()
    {
        var previous = Observation(
            automationIdentity: 10,
            targetBounds: new ScreenRect(100, 100, 800, 600));
        var current = Observation(
            automationIdentity: 20,
            targetBounds: new ScreenRect(101, 100, 800, 600));

        Assert.True(InputTargetContinuity.IsSameTarget(current, previous));
    }

    [Fact]
    public void DisjointEditorsWithTheSameProviderShapeAreDifferentTargets()
    {
        var previous = Observation(
            automationIdentity: 10,
            targetBounds: new ScreenRect(100, 100, 400, 40));
        var current = Observation(
            automationIdentity: 20,
            targetBounds: new ScreenRect(100, 300, 400, 40));

        Assert.False(InputTargetContinuity.IsSameTarget(current, previous));
    }

    [Fact]
    public void SameRuntimeIdRemainsTheSameTargetWhenTheEditorMoves()
    {
        var previous = Observation(
            automationIdentity: 10,
            targetBounds: new ScreenRect(100, 100, 800, 600));
        var current = Observation(
            automationIdentity: 10,
            targetBounds: new ScreenRect(900, 100, 800, 600));

        Assert.True(InputTargetContinuity.IsSameTarget(current, previous));
    }

    [Fact]
    public void OneHundredRuntimeIdChangesInsideTheSameEditorStayContinuous()
    {
        var previous = Observation(
            automationIdentity: 1,
            targetBounds: new ScreenRect(100, 100, 800, 600));

        for (var index = 2; index <= 101; index++)
        {
            var current = Observation(
                automationIdentity: index,
                targetBounds: new ScreenRect(
                    100 + index % 2,
                    100 + index % 3,
                    800,
                    600));

            Assert.True(InputTargetContinuity.IsSameTarget(current, previous));
            previous = current;
        }
    }

    private static RawInputContextObservation Observation(
        int automationIdentity,
        ScreenRect targetBounds) =>
        new(
            1,
            2,
            automationIdentity,
            new TargetDescriptor(
                3,
                "Code",
                "ControlType.Edit",
                "cm-content cm-lineWrapping",
                "Chrome"),
            InputState.Chinese,
            InputStateEvidence.Unavailable,
            new InputEvidence(
                true,
                false,
                false,
                new ScreenRect(200, 220, 2, 20),
                null,
                null),
            UiAutomationCaretMethod.TextPattern,
            TextPattern2Status.PatternUnavailable,
            1)
        {
            TargetBounds = targetBounds,
        };
}
