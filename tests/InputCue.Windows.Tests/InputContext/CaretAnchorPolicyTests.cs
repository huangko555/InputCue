using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class CaretAnchorPolicyTests
{
    [Theory]
    [InlineData(2, 20)]
    [InlineData(8, 16)]
    public void KeepsNarrowCaretRectangles(double width, double height)
    {
        var caret = new ScreenRect(320, 240, width, height);

        Assert.Equal(caret, CaretAnchorPolicy.KeepCaretLike(caret));
    }

    [Fact]
    public void RejectsTheWideLineRectangleReportedWhileVsCodeScrolls()
    {
        var transientLineBounds = new ScreenRect(370, 180, 750, 20);

        Assert.Null(CaretAnchorPolicy.KeepCaretLike(transientLineBounds));
    }

    [Fact]
    public void RejectsAnImplausiblyTallCaretRectangle()
    {
        var editorBounds = new ScreenRect(100, 100, 2, 600);

        Assert.Null(CaretAnchorPolicy.KeepCaretLike(editorBounds));
    }
}
