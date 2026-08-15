using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class DefaultImeWindowProbeTests
{
    [Fact]
    public void ObserveReportsAnUnavailableDefaultWindowWithoutSendingMessages()
    {
        var native = new StubDefaultImeWindowNative(defaultImeWindow: 0);
        var probe = new DefaultImeWindowProbe(native);

        var facts = probe.Observe(42);

        Assert.False(facts.HasDefaultImeWindow);
        Assert.Null(facts.OpenStatus);
        Assert.Null(facts.ConversionMode);
        Assert.Empty(native.Commands);
    }

    [Fact]
    public void ObservePreservesSuccessfulZeroValues()
    {
        var native = new StubDefaultImeWindowNative(
            defaultImeWindow: 99,
            new ImeWindowMessageResult(true, 0),
            new ImeWindowMessageResult(true, 0));
        var probe = new DefaultImeWindowProbe(native);

        var facts = probe.Observe(42);

        Assert.True(facts.HasDefaultImeWindow);
        Assert.Equal((uint)0, facts.OpenStatus);
        Assert.Equal((uint)0, facts.ConversionMode);
        Assert.Equal(
            [DefaultImeWindowProbe.GetOpenStatus, DefaultImeWindowProbe.GetConversionMode],
            native.Commands);
        Assert.All(native.Timeouts, timeout =>
            Assert.InRange(timeout, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(25)));
    }

    [Fact]
    public void ObserveKeepsFailedMessagesDistinctFromSuccessfulZeroValues()
    {
        var native = new StubDefaultImeWindowNative(
            defaultImeWindow: 99,
            new ImeWindowMessageResult(false, 0),
            new ImeWindowMessageResult(true, 1));
        var probe = new DefaultImeWindowProbe(native);

        var facts = probe.Observe(42);

        Assert.True(facts.HasDefaultImeWindow);
        Assert.Null(facts.OpenStatus);
        Assert.Equal((uint)1, facts.ConversionMode);
    }

    private sealed class StubDefaultImeWindowNative(
        nint defaultImeWindow,
        params ImeWindowMessageResult[] results) : IDefaultImeWindowNative
    {
        private readonly Queue<ImeWindowMessageResult> _results = new(results);

        internal List<uint> Commands { get; } = [];

        internal List<TimeSpan> Timeouts { get; } = [];

        public nint GetDefaultImeWindow(nint targetWindow) => defaultImeWindow;

        public ImeWindowMessageResult Query(
            nint imeWindow,
            uint command,
            TimeSpan timeout)
        {
            Commands.Add(command);
            Timeouts.Add(timeout);
            return _results.Dequeue();
        }
    }
}
