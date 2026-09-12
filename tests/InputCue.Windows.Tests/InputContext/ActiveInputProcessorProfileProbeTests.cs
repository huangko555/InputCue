using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class ActiveInputProcessorProfileProbeTests
{
    [Fact]
    public void ObserveReturnsTheActiveKeyboardProfile()
    {
        var expected = new InputProcessorProfileIdentity(Guid.NewGuid(), Guid.NewGuid());
        var probe = new ActiveInputProcessorProfileProbe(new StubNative(true, expected));

        var actual = probe.Observe();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ObserveReturnsUnavailableWhenTheNativeQueryFails()
    {
        var probe = new ActiveInputProcessorProfileProbe(new StubNative(false, default));

        var actual = probe.Observe();

        Assert.False(actual.IsAvailable);
    }

    private sealed class StubNative(
        bool succeeded,
        InputProcessorProfileIdentity profile) : IActiveInputProcessorProfileNative
    {
        public bool TryGetActiveKeyboardProfile(out InputProcessorProfileIdentity activeProfile)
        {
            activeProfile = profile;
            return succeeded;
        }
    }
}
