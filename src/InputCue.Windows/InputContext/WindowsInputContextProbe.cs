using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Accessibility;
using InputCue.Core.InputContext;
using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

internal sealed class WindowsInputContextProbe : IDisposable
{
    private readonly NativeTextPattern2CaretProbe _textPattern2CaretProbe = new();
    private readonly WindowsInputStateProbe _inputStateProbe = new();

    internal RawInputContextObservation Observe()
    {
        var startedAt = Stopwatch.GetTimestamp();
        var foregroundWindow = NativeMethods.GetForegroundWindow();

        if (foregroundWindow == 0)
        {
            return Failure(foregroundWindow, 0, ProbeIssue.SourceUnavailable, startedAt);
        }

        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
        var threadInfo = GuiThreadInfo.Create();
        _ = NativeMethods.GetGUIThreadInfo(foregroundThread, ref threadInfo);

        try
        {
            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement is null)
            {
                return Failure(foregroundWindow, threadInfo.FocusWindow, ProbeIssue.SourceUnavailable, startedAt);
            }

            var current = focusedElement.Current;
            var focusedProcessId = current.ProcessId;
            var focusWindow = threadInfo.FocusWindow;
            var automationIdentity = AutomationIdentity(focusedElement);

            if (focusedProcessId <= 0 || focusedProcessId != foregroundProcessId)
            {
                return Failure(
                    foregroundWindow,
                    focusWindow,
                    ProbeIssue.ConflictingEvidence,
                    startedAt,
                    focusedProcessId);
            }

            if (focusWindow != 0)
            {
                _ = NativeMethods.GetWindowThreadProcessId(focusWindow, out var focusWindowProcessId);
                if (focusWindowProcessId != foregroundProcessId)
                {
                    return Failure(
                        foregroundWindow,
                        focusWindow,
                        ProbeIssue.ConflictingEvidence,
                        startedAt,
                        focusedProcessId);
                }
            }

            var textPattern = GetPattern<TextPattern>(focusedElement, TextPattern.Pattern);
            var valuePattern = GetPattern<ValuePattern>(focusedElement, ValuePattern.Pattern);
            var isReadOnly = ReadOnlyState(textPattern, valuePattern);
            var hasEditableFocus = current.HasKeyboardFocus &&
                current.IsEnabled &&
                isReadOnly is false &&
                EditableControlPolicy.SupportsTextEditing(
                    current.ControlType,
                    valuePattern is not null);
            var textObservation = ObserveText(textPattern, includeSelectionCaret: hasEditableFocus);
            var shouldProbeCaret = hasEditableFocus;
            var textPattern2 = shouldProbeCaret
                ? _textPattern2CaretProbe.TryGetCaret()
                : new NativeTextPattern2CaretProbe.NativeCaretResult(
                    null,
                    TextPattern2Status.NotAttempted);
            var requiresCaretShape = textObservation.HasSelection is true;
            ScreenRect? textPattern2Caret = shouldProbeCaret &&
                textPattern2.Caret is { } nativeCaret &&
                (!requiresCaretShape || IsCaretLike(nativeCaret))
                    ? nativeCaret
                    : null;
            ScreenRect? textPatternCaret = shouldProbeCaret &&
                textObservation.Caret is { } managedCaret &&
                (!requiresCaretShape || IsCaretLike(managedCaret))
                    ? managedCaret
                    : null;
            var uiAutomationCaret = textPattern2Caret ?? textPatternCaret;
            var uiAutomationCaretMethod = textPattern2Caret is not null
                ? UiAutomationCaretMethod.TextPattern2
                : textPatternCaret is not null
                    ? UiAutomationCaretMethod.TextPattern
                : UiAutomationCaretMethod.None;
            var win32Caret = shouldProbeCaret ? Win32Caret(threadInfo) : null;
            var msaaCaret = shouldProbeCaret
                ? MsaaCaret(focusWindow == 0 ? foregroundWindow : focusWindow)
                : null;
            var hasCaret = uiAutomationCaret is { IsUsable: true } ||
                win32Caret is { IsUsable: true } ||
                msaaCaret is { IsUsable: true };
            var inputState = hasEditableFocus && hasCaret
                ? _inputStateProbe.Observe(focusWindow == 0 ? foregroundWindow : focusWindow)
                : InputStateObservation.Unknown;

            var target = new TargetDescriptor(
                focusedProcessId,
                ProcessName(focusedProcessId),
                current.ControlType?.ProgrammaticName ?? string.Empty,
                current.ClassName ?? string.Empty,
                current.FrameworkId ?? string.Empty);
            var evidence = new InputEvidence(
                hasEditableFocus,
                isReadOnly,
                textObservation.HasSelection,
                uiAutomationCaret,
                win32Caret,
                msaaCaret);

            var initialIdentity = new ObservationIdentity(
                foregroundWindow,
                foregroundProcessId,
                focusWindow,
                focusedProcessId,
                automationIdentity);
            if (!IsStillCurrent(initialIdentity))
            {
                return Failure(
                    foregroundWindow,
                    focusWindow,
                    ProbeIssue.ConflictingEvidence,
                    startedAt,
                    focusedProcessId);
            }

            if (!_inputStateProbe.IsCurrent(inputState))
            {
                return Failure(
                    foregroundWindow,
                    focusWindow,
                    ProbeIssue.ConflictingEvidence,
                    startedAt,
                    focusedProcessId);
            }

            return new RawInputContextObservation(
                foregroundWindow,
                focusWindow,
                automationIdentity,
                target,
                inputState.State,
                inputState.Evidence,
                evidence,
                uiAutomationCaretMethod,
                textPattern2.Status,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                foregroundWindow,
                threadInfo.FocusWindow,
                ProbeIssue.InsufficientPrivilege,
                startedAt,
                (int)foregroundProcessId);
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80070005)
        {
            return Failure(
                foregroundWindow,
                threadInfo.FocusWindow,
                ProbeIssue.InsufficientPrivilege,
                startedAt,
                (int)foregroundProcessId);
        }
        catch (Exception exception) when (IsExpectedProbeFailure(exception))
        {
            return Failure(
                foregroundWindow,
                threadInfo.FocusWindow,
                ProbeIssue.SourceUnavailable,
                startedAt,
                (int)foregroundProcessId);
        }
    }

    private static TPattern? GetPattern<TPattern>(AutomationElement element, AutomationPattern pattern)
        where TPattern : class =>
        element.TryGetCurrentPattern(pattern, out var value) ? value as TPattern : null;

    public void Dispose() => _textPattern2CaretProbe.Dispose();

    private static bool? ReadOnlyState(TextPattern? textPattern, ValuePattern? valuePattern)
    {
        if (valuePattern is not null)
        {
            return valuePattern.Current.IsReadOnly;
        }

        if (textPattern is null)
        {
            return null;
        }

        var value = textPattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        return value is bool isReadOnly ? isReadOnly : null;
    }

    private static TextObservation ObserveText(
        TextPattern? textPattern,
        bool includeSelectionCaret)
    {
        if (textPattern is null)
        {
            return new TextObservation(null, null);
        }

        var hasSelection = false;
        ScreenRect? caret = null;
        foreach (var range in textPattern.GetSelection())
        {
            var comparison = range.CompareEndpoints(
                TextPatternRangeEndpoint.Start,
                range,
                TextPatternRangeEndpoint.End);
            if (comparison != 0)
            {
                hasSelection = true;
                if (includeSelectionCaret)
                {
                    caret ??= SelectionEndCaret(range);
                }
            }
            else if (caret is null)
            {
                caret = FirstRectangle(range.GetBoundingRectangles());
            }
        }

        return new TextObservation(hasSelection, caret);
    }

    private static ScreenRect? SelectionEndCaret(TextPatternRange range)
    {
        try
        {
            var collapsed = range.Clone();
            collapsed.MoveEndpointByRange(
                TextPatternRangeEndpoint.Start,
                collapsed,
                TextPatternRangeEndpoint.End);
            var caret = FirstRectangle(collapsed.GetBoundingRectangles());
            return caret is { } value && IsCaretLike(value) ? value : null;
        }
        catch (Exception exception) when (IsExpectedProbeFailure(exception))
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static bool IsCaretLike(ScreenRect rectangle) =>
        rectangle.IsUsable &&
        rectangle.Width <= Math.Max(8, rectangle.Height / 2) &&
        rectangle.Height <= 256;

    private static int AutomationIdentity(AutomationElement element)
    {
        var hash = new HashCode();
        foreach (var value in element.GetRuntimeId())
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    private static bool IsStillCurrent(ObservationIdentity initial)
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(
            foregroundWindow,
            out var foregroundProcessId);
        if (foregroundThread == 0)
        {
            return false;
        }

        var finalThreadInfo = GuiThreadInfo.Create();
        if (!NativeMethods.GetGUIThreadInfo(foregroundThread, ref finalThreadInfo))
        {
            return false;
        }

        var finalFocusedElement = AutomationElement.FocusedElement;
        if (finalFocusedElement is null)
        {
            return false;
        }

        var current = new ObservationIdentity(
            foregroundWindow,
            foregroundProcessId,
            finalThreadInfo.FocusWindow,
            finalFocusedElement.Current.ProcessId,
            AutomationIdentity(finalFocusedElement));
        return ObservationValidator.IsCurrent(initial, current);
    }

    private static ScreenRect? FirstRectangle(System.Windows.Rect[] rectangles)
    {
        foreach (var source in rectangles)
        {
            var width = source.Width;
            var height = source.Height;
            if (double.IsFinite(height) && height > 0 && width == 0)
            {
                width = 1;
            }

            var rectangle = new ScreenRect(source.X, source.Y, width, height);
            if (rectangle.IsUsable)
            {
                return rectangle;
            }
        }

        return null;
    }

    private static ScreenRect? Win32Caret(GuiThreadInfo threadInfo)
    {
        if (threadInfo.CaretWindow == 0)
        {
            return null;
        }

        var topLeft = new NativePoint
        {
            X = threadInfo.CaretRectangle.Left,
            Y = threadInfo.CaretRectangle.Top,
        };
        var bottomRight = new NativePoint
        {
            X = threadInfo.CaretRectangle.Right,
            Y = threadInfo.CaretRectangle.Bottom,
        };

        if (!NativeMethods.ClientToScreen(threadInfo.CaretWindow, ref topLeft) ||
            !NativeMethods.ClientToScreen(threadInfo.CaretWindow, ref bottomRight))
        {
            return null;
        }

        var width = Math.Max(1, bottomRight.X - topLeft.X);
        var height = bottomRight.Y - topLeft.Y;
        var rectangle = new ScreenRect(topLeft.X, topLeft.Y, width, height);
        return rectangle.IsUsable ? rectangle : null;
    }

    private static ScreenRect? MsaaCaret(nint window)
    {
        object? accessibleObject = null;
        try
        {
            var interfaceId = NativeMethods.IAccessibleId;
            var result = NativeMethods.AccessibleObjectFromWindow(
                window,
                NativeMethods.ObjectIdCaret,
                ref interfaceId,
                out accessibleObject);
            if (result < 0 || accessibleObject is not IAccessible accessible)
            {
                return null;
            }

            accessible.accLocation(out var left, out var top, out var width, out var height, 0);
            var rectangle = new ScreenRect(left, top, Math.Max(1, width), height);
            return rectangle.IsUsable ? rectangle : null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (accessibleObject is not null && Marshal.IsComObject(accessibleObject))
            {
                _ = Marshal.ReleaseComObject(accessibleObject);
            }
        }
    }

    private static string ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static bool IsExpectedProbeFailure(Exception exception) =>
        exception is ElementNotAvailableException or
            ElementNotEnabledException or
            InvalidOperationException or
            NotSupportedException;

    private static RawInputContextObservation Failure(
        nint foregroundWindow,
        nint focusWindow,
        ProbeIssue issue,
        long startedAt,
        int processId = 0)
    {
        var target = new TargetDescriptor(
            processId,
            processId > 0 ? ProcessName(processId) : string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
        var evidence = new InputEvidence(false, null, null, null, null, null, issue);

        return new RawInputContextObservation(
            foregroundWindow,
            focusWindow,
            0,
            target,
            InputState.Unknown,
            InputStateEvidence.Unavailable,
            evidence,
            UiAutomationCaretMethod.None,
            TextPattern2Status.NotAttempted,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private sealed record TextObservation(bool? HasSelection, ScreenRect? Caret);
}
