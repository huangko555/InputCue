namespace InputCue.Core.InputContext;

public static class EffectiveInputState
{
    public static InputState Resolve(InputState inputState, bool capsLockEnabled) =>
        capsLockEnabled ? InputState.CapsLock : inputState;
}
