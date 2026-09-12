using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class WindowsInputStateProbeTests
{
    [Fact]
    public void ObserveReturnsEnglishUsForTheUsKeyboardLayout()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(7, 0x0409, false));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.EnglishUs, observation.State);
        Assert.Equal((nint)42, observation.TargetWindow);
        Assert.Equal((uint)7, observation.ThreadId);
        Assert.Equal((nint)0x0409, observation.KeyboardLayout);
    }

    [Fact]
    public void ObserveReturnsEnglishUsWhenTheUsKeyboardProfileIsReportedAsIme()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0409,
                true,
                HasImeContext: false,
                DefaultImeWindow: new DefaultImeWindowFacts(true, 0, 1)));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.EnglishUs, observation.State);
    }

    [Fact]
    public void ObserveKeepsOtherStableNonImeLayoutsAsEnglish()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(7, 0x0809, false));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.English, observation.State);
    }

    [Fact]
    public void ObserveClassifiesChineseImeFromConsistentNativeModeEvidence()
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

        Assert.Equal(InputState.Chinese, observation.State);
        Assert.Equal((ushort)0x0804, observation.Evidence.LanguageId);
        Assert.True(observation.Evidence.IsIme);
        Assert.True(observation.Evidence.HasImeContext);
        Assert.True(observation.Evidence.ImeOpen);
        Assert.Equal((uint)0x0001, observation.Evidence.ConversionMode);
        Assert.True(observation.Evidence.HasDefaultImeWindow);
        Assert.Equal((uint)1, observation.Evidence.ImeWindowOpenStatus);
        Assert.Equal((uint)0x0401, observation.Evidence.ImeWindowConversionMode);
    }

    [Fact]
    public void ObserveClassifiesChineseImeAsEnglishWhenNativeModeIsOff()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0804,
                true,
                HasImeContext: false,
                DefaultImeWindow: new DefaultImeWindowFacts(true, 1, 0)));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.English, observation.State);
    }

    [Fact]
    public void ObserveClassifiesWechatImeAsChineseWhenOpenStatusIsOn()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0804,
                true,
                HasImeContext: false,
                DefaultImeWindow: new DefaultImeWindowFacts(true, 1, 1),
                ActiveProfile: new InputProcessorProfileIdentity(
                    new Guid("86598fb9-66a2-463e-b9c2-aeb906d477ad"),
                    new Guid("607fdf85-fcc8-4dbd-a365-41296f980c9c"))));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.Chinese, observation.State);
        Assert.Equal(
            new Guid("86598fb9-66a2-463e-b9c2-aeb906d477ad"),
            observation.Evidence.InputProcessorClassId);
        Assert.Equal(
            new Guid("607fdf85-fcc8-4dbd-a365-41296f980c9c"),
            observation.Evidence.InputProcessorProfileId);
    }

    [Fact]
    public void ObserveClassifiesWechatImeAsEnglishWhenOpenStatusIsOff()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0804,
                true,
                HasImeContext: false,
                DefaultImeWindow: new DefaultImeWindowFacts(true, 0, 1),
                ActiveProfile: new InputProcessorProfileIdentity(
                    new Guid("86598fb9-66a2-463e-b9c2-aeb906d477ad"),
                    new Guid("607fdf85-fcc8-4dbd-a365-41296f980c9c"))));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.English, observation.State);
    }

    [Fact]
    public void ObserveKeepsUnsupportedImeLayoutsUnknown()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0411,
                true,
                HasImeContext: false,
                DefaultImeWindow: new DefaultImeWindowFacts(true, 1, 1)));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.Unknown, observation.State);
    }

    [Fact]
    public void ObserveKeepsConflictingImeSourcesUnknown()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0804,
                true,
                HasImeContext: true,
                ImeOpen: true,
                ConversionMode: 0,
                DefaultImeWindow: new DefaultImeWindowFacts(true, 1, 1)));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.Unknown, observation.State);
    }

    [Fact]
    public void ObserveKeepsImeUnknownWithoutOpenStatus()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0804,
                true,
                HasImeContext: false,
                DefaultImeWindow: new DefaultImeWindowFacts(true, null, 1)));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.Unknown, observation.State);
    }

    [Fact]
    public void ObserveKeepsImeUnknownWithoutConversionMode()
    {
        var reader = new StubInputStateFactsReader(
            new InputStateFacts(
                7,
                0x0804,
                true,
                HasImeContext: false,
                DefaultImeWindow: new DefaultImeWindowFacts(true, 1, null)));
        var probe = new WindowsInputStateProbe(reader);

        var observation = probe.Observe(42);

        Assert.Equal(InputState.Unknown, observation.State);
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
