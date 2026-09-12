namespace InputCue.Windows.InputContext;

using InputCue.Core.InputContext;

internal static class InputTargetContinuity
{
    private const double MinimumOverlapOfSmallerTarget = 0.8;

    internal static bool IsSameTarget(
        RawInputContextObservation current,
        RawInputContextObservation? previous)
    {
        if (previous is null)
        {
            return false;
        }

        if (current.Fingerprint == previous.Fingerprint)
        {
            return true;
        }

        if (current.ForegroundWindow != previous.ForegroundWindow ||
            current.FocusWindow != previous.FocusWindow ||
            current.Target.ProcessId != previous.Target.ProcessId ||
            current.Evidence.HasEditableFocus != previous.Evidence.HasEditableFocus ||
            !current.Evidence.HasEditableFocus ||
            !HasSameProviderShape(current.Target, previous.Target) ||
            current.TargetBounds is not { IsUsable: true } currentBounds ||
            previous.TargetBounds is not { IsUsable: true } previousBounds)
        {
            return false;
        }

        // Chromium providers can replace a focused AutomationElement while the
        // semantic editor stays unchanged. A nearly identical editor rectangle
        // is stable across caret moves, while separate input boxes are disjoint.
        return OverlapOfSmallerTarget(currentBounds, previousBounds) >=
            MinimumOverlapOfSmallerTarget;
    }

    private static bool HasSameProviderShape(TargetDescriptor current, TargetDescriptor previous) =>
        string.Equals(current.ControlType, previous.ControlType, StringComparison.Ordinal) &&
        string.Equals(current.ClassName, previous.ClassName, StringComparison.Ordinal) &&
        string.Equals(current.FrameworkId, previous.FrameworkId, StringComparison.Ordinal);

    private static double OverlapOfSmallerTarget(ScreenRect first, ScreenRect second)
    {
        var intersectionWidth = Math.Max(
            0,
            Math.Min(first.X + first.Width, second.X + second.Width) -
            Math.Max(first.X, second.X));
        var intersectionHeight = Math.Max(
            0,
            Math.Min(first.Y + first.Height, second.Y + second.Height) -
            Math.Max(first.Y, second.Y));
        var smallerArea = Math.Min(
            first.Width * first.Height,
            second.Width * second.Height);
        return smallerArea > 0
            ? intersectionWidth * intersectionHeight / smallerArea
            : 0;
    }
}
