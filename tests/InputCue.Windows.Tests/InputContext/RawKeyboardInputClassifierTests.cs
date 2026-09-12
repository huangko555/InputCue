using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class RawKeyboardInputClassifierTests
{
    [Theory]
    [InlineData(0x41)]
    [InlineData(0x30)]
    [InlineData(0x60)]
    [InlineData(0xBA)]
    [InlineData(0x08)]
    [InlineData(0x0D)]
    [InlineData(0x20)]
    [InlineData(0x2E)]
    [InlineData(0xE5)]
    [InlineData(0xE7)]
    public void EditingKeysAreRecognized(ushort virtualKey)
    {
        Assert.True(RawKeyboardInputClassifier.IsEditingKey(virtualKey));
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x14)]
    [InlineData(0x21)]
    [InlineData(0x22)]
    [InlineData(0x23)]
    [InlineData(0x24)]
    [InlineData(0x25)]
    [InlineData(0x26)]
    [InlineData(0x27)]
    [InlineData(0x28)]
    [InlineData(0x2D)]
    [InlineData(0x70)]
    [InlineData(0x5B)]
    public void NonEditingKeysAreIgnored(ushort virtualKey)
    {
        Assert.False(RawKeyboardInputClassifier.IsEditingKey(virtualKey));
    }
}
