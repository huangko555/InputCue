using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.InputContext;

public sealed class ScreenRectTests
{
    [Fact]
    public void IsUsableReturnsTrueForFiniteRectangleWithPositiveSize()
    {
        var rectangle = new ScreenRect(10, 20, 2, 18);

        Assert.True(rectangle.IsUsable);
    }

    [Theory]
    [InlineData(double.NaN, 0, 1, 1)]
    [InlineData(0, double.PositiveInfinity, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, -1)]
    public void IsUsableReturnsFalseWhenRectangleCannotAnchorIndicator(
        double x,
        double y,
        double width,
        double height)
    {
        var rectangle = new ScreenRect(x, y, width, height);

        Assert.False(rectangle.IsUsable);
    }
}
