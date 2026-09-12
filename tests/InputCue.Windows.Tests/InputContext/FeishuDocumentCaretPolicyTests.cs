using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class FeishuDocumentCaretPolicyTests
{
    private static readonly ScreenRect Viewport = new(100, 100, 800, 600);

    [Theory]
    [InlineData(100)]
    [InlineData(106)]
    [InlineData(676)]
    public void RejectsAFeishuCaretClampedToAVerticalViewportEdge(double y)
    {
        var caret = new ScreenRect(240, y, 2, 24);

        Assert.Null(FeishuDocumentCaretPolicy.KeepUnclamped(
            caret,
            Viewport,
            isFeishuDocument: true));
    }

    [Fact]
    public void KeepsAFeishuCaretAwayFromTheViewportEdges()
    {
        var caret = new ScreenRect(240, 320, 2, 24);

        Assert.Equal(caret, FeishuDocumentCaretPolicy.KeepUnclamped(
            caret,
            Viewport,
            isFeishuDocument: true));
    }

    [Fact]
    public void DoesNotChangeOtherWebEditorsAtTheViewportEdge()
    {
        var caret = new ScreenRect(240, 100, 2, 24);

        Assert.Equal(caret, FeishuDocumentCaretPolicy.KeepUnclamped(
            caret,
            Viewport,
            isFeishuDocument: false));
    }
}
