using InputCue.Core.InputContext;

namespace InputCue.Core.Indicator;

/// <summary>
/// Limits context-established prompts within one continuous stay in an application.
/// The caller supplies observations from its existing foreground/focus event stream.
/// </summary>
public sealed class AppStayPromptPolicy
{
    private ApplicationIdentity? _currentApplication;
    private bool _hasPromptOpportunity;

    public bool ShouldSuppressContextReplay(
        TargetDescriptor target,
        InputContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);

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
        }

        if (!IsPromptOpportunity(snapshot))
        {
            return false;
        }

        if (_hasPromptOpportunity)
        {
            return true;
        }

        _hasPromptOpportunity = true;
        return false;
    }

    public void Reset()
    {
        _currentApplication = null;
        _hasPromptOpportunity = false;
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
