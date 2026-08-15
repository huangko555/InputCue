using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class WindowsInputStateProbeTests
{
    [Fact]
    public void ObserveReturnsEnglishForAStableNonImeLayout()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(7, 0x0409, false));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.English, observation.State);
        Assert.Equal((nint)42, observation.TargetWindow);
        Assert.Equal((uint)7, observation.ThreadId);
        Assert.Equal((nint)0x0409, observation.KeyboardLayout);
    }

    [Fact]
    public void ObserveKeepsImeLayoutsUnknownUntilAProfileIsVerified()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0804,
                true,
                HasImeContext: true,
                ImeOpen: true,
                ConversionMode: 0x0001,
                DefaultImeWindow: new DefaultImeWindowFacts(true, 1, 0x0401)));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.Unknown, observation.State);
        Assert.Equal((ushort)0x0804, observation.Evidence.LanguageId);
        Assert.True(observation.Evidence.IsIme);
        Assert.True(observation.Evidence.HasImeContext);
        Assert.True(observation.Evidence.ImeOpen);
        Assert.Equal((uint)0x0001, observation.Evidence.ConversionMode);
        Assert.True(observation.Evidence.HasDefaultImeWindow);
        Assert.Equal((uint)1, observation.Evidence.ImeWindowOpenStatus);
        Assert.Equal((uint)0x0401, observation.Evidence.ImeWindowConversionMode);
    }

    [Theory]
    [InlineData(0, 0x0409)]
    [InlineData(7, 0)]
    public void ObserveReturnsUnknownWhenNativeIdentityIsIncomplete(
        uint threadId,
        long keyboardLayout)
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(threadId, (nint)keyboardLayout, false));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.Unknown, observation.State);
    }

    [Fact]
    public void IsCurrentRejectsAKeyboardLayoutChange()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(7, 0x0409, false),
            new InputStateFacts(7, 0x0804, true));
        var probe = new WindowsInputStateProbe(reader);
        var observation = probe.Observe(42);

        var isCurrent = probe.IsCurrent(observation);

        Assert.False(isCurrent);
    }

    [Fact]
    public void IsCurrentRejectsAnImeConversionModeChange()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(7, 0x0804, true, true, true, 0x0001),
            new InputStateFacts(7, 0x0804, true, true, true, 0x0000));
        var probe = new WindowsInputStateProbe(reader);
        var observation = probe.Observe(42);

        var isCurrent = probe.IsCurrent(observation);

        Assert.False(isCurrent);
    }

    [Fact]
    public void IsCurrentAcceptsTheSameTargetThreadAndKeyboardLayout()
    {
        var facts = new InputStateFacts(7, 0x0409, false);
        var reader = new StubInputStateFactsReader(facts, facts);
        var probe = new WindowsInputStateProbe(reader);
        var observation = probe.Observe(42);

        var isCurrent = probe.IsCurrent(observation);

        Assert.True(isCurrent);
    }

    private sealed class StubInputStateFactsReader(params InputStateFacts[] facts)
        : IInputStateFactsReader
    {
        private readonly Queue<InputStateFacts> _facts = new(facts);
        private InputStateFacts _last = facts.LastOrDefault();

        public InputStateFacts Read(nint targetWindow)
        {
            if (_facts.TryDequeue(out var next))
            {
                _last = next;
            }

            return _last;
        }
    }
}
