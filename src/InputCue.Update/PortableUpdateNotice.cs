namespace InputCue.Update;

public enum PortableUpdateNoticeTone
{
    Information,
    Success,
    Warning,
    Error,
}

public sealed record PortableUpdateNotice(
    PortableUpdateNoticeTone Tone,
    string Message,
    TimeSpan Duration)
{
    public static PortableUpdateNotice Checking { get; } = new(
        PortableUpdateNoticeTone.Information,
        "正在检查更新…",
        TimeSpan.Zero);

    public static PortableUpdateNotice FromResult(PortableUpdateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            PortableUpdateStatus.Deferred => new(
                PortableUpdateNoticeTone.Information,
                result.Message,
                TimeSpan.FromMilliseconds(2500)),
            PortableUpdateStatus.Unsupported => new(
                PortableUpdateNoticeTone.Warning,
                result.Message,
                TimeSpan.FromMilliseconds(4000)),
            PortableUpdateStatus.UpToDate => new(
                PortableUpdateNoticeTone.Success,
                result.Message,
                TimeSpan.FromMilliseconds(2500)),
            PortableUpdateStatus.UpdateReady => new(
                PortableUpdateNoticeTone.Success,
                result.Message,
                TimeSpan.FromMilliseconds(1200)),
            PortableUpdateStatus.Failed => new(
                PortableUpdateNoticeTone.Error,
                result.Message,
                TimeSpan.FromMilliseconds(5000)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null),
        };
    }
}
