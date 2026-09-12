using InputCue.Core.InputContext;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class AnchorVisibilityPolicyTests
{
    private static readonly ScreenRect Viewport = new(100, 100, 800, 600);

    [Fact]
    public void KeepsACaretInsideTheVisibleViewport()
    {
        var caret = new ScreenRect(240, 320, 2, 24);

        Assert.Equal(caret, AnchorVisibilityPolicy.KeepVisible(caret, Viewport));
    }

    [Theory]
    [InlineData(240, 70)]
    [InlineData(240, 701)]
    [InlineData(70, 320)]
    [InlineData(901, 320)]
    public void RejectsACaretThatScrolledOutsideTheVisibleViewport(double x, double y)
    {
        var caret = new ScreenRect(x, y, 2, 24);

        Assert.Null(AnchorVisibilityPolicy.KeepVisible(caret, Viewport));
    }

    [Fact]
    public void KeepsAPartiallyClippedCaretAtTheViewportEdge()
    {
        var caret = new ScreenRect(240, 90, 2, 24);

        Assert.Equal(caret, AnchorVisibilityPolicy.KeepVisible(caret, Viewport));
    }

    [Fact]
    public void MissingViewportDoesNotDisableAnOtherwiseValidCaret()
    {
        var caret = new ScreenRect(240, 320, 2, 24);

        Assert.Equal(caret, AnchorVisibilityPolicy.KeepVisible(caret, visibleBounds: null));
    }

    [Fact]
    public void KnownEmptyViewportRejectsTheCaret()
    {
        var caret = new ScreenRect(240, 320, 2, 24);

        Assert.Null(AnchorVisibilityPolicy.KeepVisible(
            caret,
            new ScreenRect(0, 0, 0, 0)));
    }

    [Fact]
    public void IntersectsTwoBoundsToFindTheirSharedVisibleArea()
    {
        var document = new ScreenRect(100, -800, 800, 2200);
        var browserContent = new ScreenRect(100, 180, 800, 620);

        var result = AnchorVisibilityPolicy.Intersect(document, browserContent);

        Assert.Equal(browserContent, result);
    }

    [Fact]
    public void ResolvesAVisibleHtmlInputFromTheWindowAndFocusedElementOnly()
    {
        var window = new ScreenRect(0, 100, 1200, 800);
        var input = new ScreenRect(240, 360, 500, 40);

        var result = AnchorVisibilityPolicy.ResolveVisibleBounds(
            window,
            input,
            targetIsOffscreen: false);

        Assert.Equal(input, result);
    }

    [Fact]
    public void ResolvesAnOffscreenFocusedElementToKnownEmptyBounds()
    {
        var window = new ScreenRect(0, 100, 1200, 800);
        var input = new ScreenRect(240, 40, 500, 40);

        var result = AnchorVisibilityPolicy.ResolveVisibleBounds(
            window,
            input,
            targetIsOffscreen: true);

        Assert.NotNull(result);
        Assert.False(result.Value.IsUsable);
    }

    [Fact]
    public void FeishuCaretPinnedToTheContentViewportTopIsRejected()
    {
        var window = new ScreenRect(0, 0, 1680, 1000);
        var documentRoot = new ScreenRect(420, 115, 820, 2400);
        var contentViewport = new ScreenRect(0, 178, 1680, 820);
        var pinnedCaret = new ScreenRect(515, 178, 1, 24);

        var visibleBounds = AnchorVisibilityPolicy.ResolveVisibleBounds(
            window,
            documentRoot,
            targetIsOffscreen: false,
            verticalClippingBounds: [contentViewport]);
        var result = FeishuDocumentCaretPolicy.KeepUnclamped(
            AnchorVisibilityPolicy.KeepVisible(pinnedCaret, visibleBounds),
            visibleBounds,
            isFeishuDocument: true);

        Assert.Null(result);
    }
}
