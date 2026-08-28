using InputCue.Core.InputContext;

namespace InputCue.Core.Indicator;

/// <summary>
/// Limits context-established prompts within one continuous stay in an application.
/// The caller supplies observations from its existing foreground/focus event stream,
/// along with the active prompt mode, replay delay, and current time.
/// </summary>
public sealed class AppStayPromptPolicy
{
    private ApplicationIdentity? _currentApplication;
    private bool _hasPromptOpportunity;
    private DateTimeOffset? _lastPromptAt;

    public bool ShouldSuppressContextReplay(
        AppStayPromptMode mode,
        TimeSpan delay,
        TargetDescriptor target,
        InputContextSnapshot snapshot,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay cannot be negative.");
        }

        if (mode is AppStayPromptMode.Always)
        {
            return false;
        }

        var application = ApplicationIdentity.From(target);
        if (application is null)
        {
            Reset();
            return false;
        }

        if (_currentApplication is null || !_currentApplication.Matches(application))
        {
            _currentApplication = application;
            _hasPromptOpportunity = false;
            _lastPromptAt = null;
        }

        if (!IsPromptOpportunity(snapshot))
        {
            return false;
        }

        if (!_hasPromptOpportunity)
        {
            _hasPromptOpportunity = true;
            _lastPromptAt = now;
            return false;
        }

        if (mode is AppStayPromptMode.AfterDelay &&
            _lastPromptAt is { } lastPromptAt &&
            now - lastPromptAt >= delay)
        {
            _lastPromptAt = now;
            return false;
        }

        return true;
    }

    public void Reset()
    {
        _currentApplication = null;
        _hasPromptOpportunity = false;
        _lastPromptAt = null;
    }

    private static bool IsPromptOpportunity(InputContextSnapshot snapshot) =>
        snapshot.Eligibility is Eligibility.EditableCaret or Eligibility.EditableSelection &&
        snapshot.InputState is not InputState.Unknown &&
        snapshot.Anchor is { IsUsable: true };

    private sealed record ApplicationIdentity(string ProcessName, int ProcessId)
    {
        internal static ApplicationIdentity? From(TargetDescriptor target)
        {
            var processName = target.ProcessName.Trim();
            if (processName.Length > 0)
            {
                return new ApplicationIdentity(processName, 0);
            }

            return target.ProcessId > 0
                ? new ApplicationIdentity(string.Empty, target.ProcessId)
                : null;
        }

        internal bool Matches(ApplicationIdentity other) =>
            ProcessId == other.ProcessId &&
            string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase);
    }
}
