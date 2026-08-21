namespace InputCue.Update;

public enum PortableUpdateStatus
{
    Deferred,
    Unsupported,
    UpToDate,
    UpdateReady,
    Failed,
}

public sealed record PortableUpdateResult(
    PortableUpdateStatus Status,
    string Message,
    string? Version = null,
    string? PackagePath = null);
