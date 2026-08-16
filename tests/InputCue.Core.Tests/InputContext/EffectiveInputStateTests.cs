using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.InputContext;

public sealed class EffectiveInputStateTests
{
    [Theory]
    [InlineData(InputState.Chinese)]
    [InlineData(InputState.English)]
    [InlineData(InputState.EnglishUs)]
    [InlineData(InputState.Unknown)]
    public void ResolveUsesCapsLockAsTheVisibleState(InputState inputState)
    {
        Assert.Equal(InputState.CapsLock, EffectiveInputState.Resolve(inputState, capsLockEnabled: true));
    }

    [Theory]
    [InlineData(InputState.Chinese)]
    [InlineData(InputState.English)]
    [InlineData(InputState.EnglishUs)]
    [InlineData(InputState.Unknown)]
    public void ResolveKeepsTheUnderlyingStateWhenCapsLockIsOff(InputState inputState)
    {
        Assert.Equal(inputState, EffectiveInputState.Resolve(inputState, capsLockEnabled: false));
    }
}
