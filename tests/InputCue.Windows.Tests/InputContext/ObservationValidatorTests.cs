using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class ObservationValidatorTests
{
    [Fact]
    public void ObservationRemainsCurrentOnlyWhenEveryIdentityMatches()
    {
        var initial = new ObservationIdentity(1, 2, 3, 4, 5);

        Assert.True(ObservationValidator.IsCurrent(initial, initial));
        Assert.False(ObservationValidator.IsCurrent(
            initial,
            initial with { ForegroundWindow = 10 }));
        Assert.False(ObservationValidator.IsCurrent(
            initial,
            initial with { ForegroundProcessId = 20 }));
        Assert.False(ObservationValidator.IsCurrent(
            initial,
            initial with { FocusWindow = 30 }));
        Assert.False(ObservationValidator.IsCurrent(
            initial,
            initial with { FocusedProcessId = 40 }));
        Assert.False(ObservationValidator.IsCurrent(
            initial,
            initial with { AutomationIdentity = 50 }));
    }

    [Fact]
    public void NativeFocusValidationIgnoresAutomationOnlyIdentityChanges()
    {
        var initial = new ObservationIdentity(1, 2, 3, 4, 5);

        Assert.True(ObservationValidator.IsNativeFocusCurrent(initial, 1, 2, 3));
        Assert.False(ObservationValidator.IsNativeFocusCurrent(initial, 10, 2, 3));
        Assert.False(ObservationValidator.IsNativeFocusCurrent(initial, 1, 20, 3));
        Assert.False(ObservationValidator.IsNativeFocusCurrent(initial, 1, 2, 30));
    }
}
