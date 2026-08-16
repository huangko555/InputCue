using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using InputCue.Core.InputContext;

namespace InputCue.Windows.InputContext;

internal sealed class WindowsInputStateRefreshProbe
{
    private readonly WindowsInputStateProbe _inputStateProbe = new();

    internal RawInputContextObservation Refresh(RawInputContextObservation current)
    {
        var startedAt = Stopwatch.GetTimestamp();
        if (current.ForegroundWindow == 0 || current.Target.ProcessId <= 0)
        {
            return Failure(ProbeIssue.ConflictingEvidence, startedAt);
        }

        var expected = new ObservationIdentity(
            current.ForegroundWindow,
            (uint)current.Target.ProcessId,
            current.FocusWindow,
            current.Target.ProcessId,
            current.AutomationElementIdentity);

        try
        {
            if (!IsCurrent(expected))
            {
                return Failure(ProbeIssue.ConflictingEvidence, startedAt);
            }

            var targetWindow = current.FocusWindow == 0
                ? current.ForegroundWindow
                : current.FocusWindow;
            var inputState = _inputStateProbe.Observe(targetWindow);
            if (!IsCurrent(expected) || !_inputStateProbe.IsCurrent(inputState))
            {
                return Failure(ProbeIssue.ConflictingEvidence, startedAt);
            }

            return current with
            {
                InputState = inputState.State,
                InputStateEvidence = inputState.Evidence,
                DurationMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            };
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(ProbeIssue.InsufficientPrivilege, startedAt);
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80070005)
        {
            return Failure(ProbeIssue.InsufficientPrivilege, startedAt);
        }
        catch (Exception exception) when (IsExpectedProbeFailure(exception))
        {
            return Failure(ProbeIssue.SourceUnavailable, startedAt);
        }
    }

    private static bool IsCurrent(ObservationIdentity expected) =>
        WindowsObservationIdentity.TryRead(out var current) &&
        ObservationValidator.IsCurrent(expected, current);

    private static RawInputContextObservation Failure(ProbeIssue issue, long startedAt) =>
        RawInputContextObservation.Failure(
            issue,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private static bool IsExpectedProbeFailure(Exception exception) =>
        exception is ElementNotAvailableException or
            ElementNotEnabledException or
            InvalidOperationException or
            NotSupportedException;
}
