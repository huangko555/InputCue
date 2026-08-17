using InputCue.Core.InputContext;

namespace InputCue.Overlay.Tests;

public sealed class LightBadgeGlyphsTests
{
    [Theory]
    [InlineData(InputState.Chinese)]
    [InlineData(InputState.English)]
    [InlineData(InputState.EnglishUs)]
    [InlineData(InputState.CapsLock)]
    public void SupportedStateUsesFrozenVectorGeometry(InputState state)
    {
        var geometry = LightBadgeGlyphs.For(state);

        Assert.False(geometry.Bounds.IsEmpty);
        Assert.True(geometry.IsFrozen);
    }

    [Fact]
    public void UnknownStateHasNoGlyph()
    {
        Assert.True(LightBadgeGlyphs.For(InputState.Unknown).Bounds.IsEmpty);
    }
}
