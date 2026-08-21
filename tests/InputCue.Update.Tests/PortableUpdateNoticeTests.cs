using InputCue.Update;

namespace InputCue.Update.Tests;

public sealed class PortableUpdateNoticeTests
{
    [Fact]
    public void CheckingNoticeIsInformationalAndPersistent()
    {
        var notice = PortableUpdateNotice.Checking;

        Assert.Equal(PortableUpdateNoticeTone.Information, notice.Tone);
        Assert.Equal("正在检查更新…", notice.Message);
        Assert.Equal(TimeSpan.Zero, notice.Duration);
    }

    [Theory]
    [InlineData(PortableUpdateStatus.Deferred, PortableUpdateNoticeTone.Information, 2500)]
    [InlineData(PortableUpdateStatus.Unsupported, PortableUpdateNoticeTone.Warning, 4000)]
    [InlineData(PortableUpdateStatus.UpToDate, PortableUpdateNoticeTone.Success, 2500)]
    [InlineData(PortableUpdateStatus.UpdateReady, PortableUpdateNoticeTone.Success, 1200)]
    [InlineData(PortableUpdateStatus.Failed, PortableUpdateNoticeTone.Error, 5000)]
    public void ResultNoticeMapsEveryUpdateStatus(
        PortableUpdateStatus status,
        PortableUpdateNoticeTone expectedTone,
        int expectedDurationMilliseconds)
    {
        var result = new PortableUpdateResult(status, "状态消息");

        var notice = PortableUpdateNotice.FromResult(result);

        Assert.Equal(expectedTone, notice.Tone);
        Assert.Equal("状态消息", notice.Message);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedDurationMilliseconds), notice.Duration);
    }
}
