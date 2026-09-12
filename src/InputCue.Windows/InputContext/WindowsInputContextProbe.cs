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
    private readonly ProcessExecutableIdentity _processExecutableIdentity = new();
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

            var focusedCurrent = focusedElement.Current;
            var focusedProcessId = focusedCurrent.ProcessId;
            var focusWindow = threadInfo.FocusWindow;
            var automationIdentity = WindowsObservationIdentity.AutomationIdentity(focusedElement);

            if (!_processExecutableIdentity.IsCompatible(foregroundProcessId, focusedProcessId))
            {
                return Failure(
                    foregroundWindow,
                    focusWindow,
                    ProbeIssue.FocusedProcessMismatch,
                    startedAt,
                    focusedProcessId);
            }

            if (focusWindow != 0)
            {
                _ = NativeMethods.GetWindowThreadProcessId(focusWindow, out var focusWindowProcessId);
                if (!_processExecutableIdentity.IsCompatible(
                    foregroundProcessId,
                    (int)focusWindowProcessId))
                {
                    return Failure(
                        foregroundWindow,
                        focusWindow,
                        ProbeIssue.FocusWindowProcessMismatch,
                        startedAt,
                        focusedProcessId);
                }
            }

            var (probeElement, isFeishuDocumentFocusProxy) = ResolveProbeElement(
                focusedElement,
                focusedCurrent);
            var current = probeElement.Current;
            var targetBounds = ToScreenRect(current.BoundingRectangle);
            var windowBounds = WindowBounds(foregroundWindow);
            var textPattern = GetPattern<TextPattern>(probeElement, TextPattern.Pattern);
            var valuePattern = GetPattern<ValuePattern>(probeElement, ValuePattern.Pattern);
            var isReadOnly = ReadOnlyState(textPattern, valuePattern);
            var processName = ProcessName(focusedProcessId);
            var isFeishuDocumentSurface = isFeishuDocumentFocusProxy ||
                AppProfileCatalog.SupportsWritableFeishuDocumentSurface(
                    current.ClassName,
                    current.FrameworkId,
                    current.ControlType,
                    textPattern is not null);
            var visibleBounds = AnchorVisibilityPolicy.ResolveVisibleBounds(
                windowBounds,
                targetBounds,
                current.IsOffscreen,
                isFeishuDocumentSurface
                    ? FeishuDocumentVerticalClippingBounds(probeElement)
                    : null);
            var hasBitableActiveEditorAncestor = HasBitableActiveEditorAncestor(probeElement);
            var hasFeishuChatParentShape = HasFeishuChatParentShape(probeElement);
            var rawBitableEditorAnchor = TryGetFeishuBitableEditorAnchor(
                probeElement,
                hasBitableActiveEditorAncestor,
                textPattern is not null);
            var bitableEditorAnchor = AnchorVisibilityPolicy.KeepVisible(
                rawBitableEditorAnchor,
                visibleBounds);
            var rawSheetCellAnchor = TryGetFeishuSheetCellAnchor(probeElement);
            var sheetCellAnchor = AnchorVisibilityPolicy.KeepVisible(
                rawSheetCellAnchor,
                visibleBounds);
            var hasFeishuSheetAncestorShape = rawSheetCellAnchor is not null;
            var hasEditableFocus = (current.HasKeyboardFocus ||
                    isFeishuDocumentFocusProxy && focusedCurrent.HasKeyboardFocus) &&
                current.IsEnabled &&
                isReadOnly is false &&
                !AppProfileCatalog.IsHiddenFeishuSelectionHelper(
                    current.ClassName,
                    current.FrameworkId,
                    current.ControlType) &&
                (EditableControlPolicy.SupportsTextEditing(
                     current.ControlType,
                     valuePattern is not null) ||
                 AppProfileCatalog.SupportsWritableWpsDocumentSurface(
                     processName,
                     current.ClassName,
                     current.FrameworkId,
                     current.ControlType,
                     valuePattern is not null) ||
                 AppProfileCatalog.SupportsWritableWindowsTerminalSurface(
                     processName,
                     current.ClassName,
                     current.FrameworkId,
                     current.ControlType) ||
                 AppProfileCatalog.SupportsWritableProseMirrorSurface(
                     current.ClassName,
                     current.FrameworkId,
                     current.ControlType,
                     textPattern is not null) ||
                 AppProfileCatalog.SupportsWritableBilibiliRichTextSurface(
                     current.ClassName,
                     current.FrameworkId,
                     current.ControlType,
                     textPattern is not null) ||
                 isFeishuDocumentSurface ||
                 AppProfileCatalog.SupportsWritableFeishuSheetSurface(
                     current.ClassName,
                     current.FrameworkId,
                     current.ControlType,
                     textPattern is not null,
                     hasFeishuSheetAncestorShape) ||
                 AppProfileCatalog.SupportsWritableFeishuBitableSurface(
                     current.ClassName,
                     current.FrameworkId,
                     current.ControlType,
                     textPattern is not null,
                     hasBitableActiveEditorAncestor) ||
                 AppProfileCatalog.SupportsWritableFeishuChatSurface(
                     processName,
                     current.ClassName,
                     current.FrameworkId,
                     current.ControlType,
                     textPattern is not null,
                     hasFeishuChatParentShape));
            var textObservation = ObserveText(textPattern, includeSelectionCaret: hasEditableFocus);
            var shouldProbeCaret = hasEditableFocus;
            var textPattern2 = shouldProbeCaret && !isFeishuDocumentFocusProxy
                ? _textPattern2CaretProbe.TryGetCaret()
                : new NativeTextPattern2CaretProbe.NativeCaretResult(
                    null,
                    TextPattern2Status.NotAttempted);
            ScreenRect? rawTextPattern2Caret = shouldProbeCaret &&
                CaretAnchorPolicy.KeepCaretLike(textPattern2.Caret) is { } nativeCaret
                    ? nativeCaret
                    : null;
            var textPattern2Caret = FeishuDocumentCaretPolicy.KeepUnclamped(
                AnchorVisibilityPolicy.KeepVisible(rawTextPattern2Caret, visibleBounds),
                visibleBounds,
                isFeishuDocumentSurface);
            ScreenRect? rawTextPatternCaret = shouldProbeCaret &&
                CaretAnchorPolicy.KeepCaretLike(textObservation.Caret) is { } managedCaret
                    ? managedCaret
                    : null;
            var textPatternCaret = FeishuDocumentCaretPolicy.KeepUnclamped(
                AnchorVisibilityPolicy.KeepVisible(rawTextPatternCaret, visibleBounds),
                visibleBounds,
                isFeishuDocumentSurface);
            var uiAutomationCaret = bitableEditorAnchor ?? sheetCellAnchor ?? textPattern2Caret ?? textPatternCaret;
            var uiAutomationCaretMethod = bitableEditorAnchor is not null || sheetCellAnchor is not null
                ? UiAutomationCaretMethod.TextPattern
                : textPattern2Caret is not null
                ? UiAutomationCaretMethod.TextPattern2
                : textPatternCaret is not null
                    ? UiAutomationCaretMethod.TextPattern
                : UiAutomationCaretMethod.None;
            var rawWin32Caret = shouldProbeCaret ? Win32Caret(threadInfo) : null;
            var win32Caret = shouldProbeCaret
                ? FeishuDocumentCaretPolicy.KeepUnclamped(
                    AnchorVisibilityPolicy.KeepVisible(rawWin32Caret, visibleBounds),
                    visibleBounds,
                    isFeishuDocumentSurface)
                : null;
            var rawMsaaCaret = shouldProbeCaret &&
                rawBitableEditorAnchor is null &&
                rawSheetCellAnchor is null
                ? MsaaCaret(focusWindow == 0 ? foregroundWindow : focusWindow)
                : null;
            var msaaCaret = rawMsaaCaret is not null
                ? FeishuDocumentCaretPolicy.KeepUnclamped(
                    AnchorVisibilityPolicy.KeepVisible(rawMsaaCaret, visibleBounds),
                    visibleBounds,
                    isFeishuDocumentSurface)
                : null;
            var hasRawCaret = rawBitableEditorAnchor is not null ||
                rawSheetCellAnchor is not null ||
                rawTextPattern2Caret is not null ||
                rawTextPatternCaret is not null ||
                rawWin32Caret is not null ||
                rawMsaaCaret is not null;
            var caretOutsideVisibleBounds = visibleBounds is not null &&
                hasRawCaret &&
                uiAutomationCaret is null &&
                win32Caret is null &&
                msaaCaret is null;
            var inputState = hasEditableFocus
                ? _inputStateProbe.Observe(focusWindow == 0 ? foregroundWindow : focusWindow)
                : InputStateObservation.Unknown;

            var target = new TargetDescriptor(
                focusedProcessId,
                processName,
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
                    ProbeIssue.ObservationIdentityChanged,
                    startedAt,
                    focusedProcessId);
            }

            if (!_inputStateProbe.IsCurrent(inputState))
            {
                return Failure(
                    foregroundWindow,
                    focusWindow,
                    ProbeIssue.InputStateEvidenceChanged,
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
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds)
            {
                TargetBounds = targetBounds,
                CaretOutsideVisibleBounds = caretOutsideVisibleBounds,
            };
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

    private static (AutomationElement Element, bool IsFeishuDocumentFocusProxy) ResolveProbeElement(
        AutomationElement focusedElement,
        AutomationElement.AutomationElementInformation focusedCurrent)
    {
        if (!focusedCurrent.HasKeyboardFocus ||
            !focusedCurrent.IsEnabled ||
            !AppProfileCatalog.IsHiddenFeishuSelectionHelper(
                focusedCurrent.ClassName,
                focusedCurrent.FrameworkId,
                focusedCurrent.ControlType))
        {
            return (focusedElement, false);
        }

        var documentSurface = FindFeishuDocumentAncestor(
            focusedElement,
            focusedCurrent,
            TreeWalker.ControlViewWalker) ??
            FindFeishuDocumentAncestor(
                focusedElement,
                focusedCurrent,
                TreeWalker.RawViewWalker);
        return documentSurface is null
            ? (focusedElement, false)
            : (documentSurface, true);
    }

    private static AutomationElement? FindFeishuDocumentAncestor(
        AutomationElement focusedElement,
        AutomationElement.AutomationElementInformation focusedCurrent,
        TreeWalker walker)
    {
        var ancestor = walker.GetParent(focusedElement);
        for (var depth = 0; depth < 24 && ancestor is not null; depth++)
        {
            var current = ancestor.Current;
            var textPattern = GetPattern<TextPattern>(ancestor, TextPattern.Pattern);
            if (current.IsEnabled &&
                !current.IsOffscreen &&
                AppProfileCatalog.SupportsFeishuDocumentFocusProxy(
                    focusedCurrent.ClassName,
                    focusedCurrent.FrameworkId,
                    focusedCurrent.ControlType,
                    current.ClassName,
                    current.FrameworkId,
                    current.ControlType,
                    textPattern is not null))
            {
                return ancestor;
            }

            ancestor = walker.GetParent(ancestor);
        }

        return null;
    }

    private static List<ScreenRect> FeishuDocumentVerticalClippingBounds(
        AutomationElement documentSurface)
    {
        var result = new List<ScreenRect>();
        try
        {
            var processId = documentSurface.Current.ProcessId;
            var ancestor = TreeWalker.RawViewWalker.GetParent(documentSurface);
            for (var depth = 0; depth < 24 && ancestor is not null; depth++)
            {
                var current = ancestor.Current;
                if (current.ProcessId != processId)
                {
                    break;
                }

                if (!current.IsOffscreen &&
                    ToScreenRect(current.BoundingRectangle) is { } bounds)
                {
                    result.Add(bounds);
                }

                if (current.ControlType == ControlType.Document)
                {
                    break;
                }

                ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
            }
        }
        catch (Exception exception) when (
            exception is COMException || IsExpectedProbeFailure(exception))
        {
            // Keep any stable ancestors already collected; the window and target bounds
            // still provide the conservative fallback used by every other profile.
        }

        return result;
    }

    private static ScreenRect? TryGetFeishuSheetCellAnchor(AutomationElement element)
    {
        if (element.Current.ControlType != ControlType.Group ||
            !string.Equals(element.Current.FrameworkId, "Chrome", StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(element.Current.ClassName))
        {
            return null;
        }

        var ancestor = TreeWalker.ControlViewWalker.GetParent(element);
        if (ancestor is null ||
            ancestor.Current.ControlType != ControlType.Group ||
            !string.IsNullOrWhiteSpace(ancestor.Current.ClassName))
        {
            return null;
        }

        var remainingAncestorTokens = new HashSet<string>(
            ["cell-wrapper-element", "suite-sheet"],
            StringComparer.Ordinal);
        var scan = ancestor;
        for (var depth = 0; depth < 6 && remainingAncestorTokens.Count > 0; depth++)
        {
            scan = TreeWalker.ControlViewWalker.GetParent(scan);
            if (scan is null)
            {
                break;
            }

            remainingAncestorTokens.RemoveWhere(classPart =>
                scan.Current.ClassName?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(classPart, StringComparer.Ordinal) is true);
        }

        if (remainingAncestorTokens.Count > 0)
        {
            return null;
        }

        var cell = ancestor.Current.BoundingRectangle;
        return double.IsFinite(cell.X) &&
            double.IsFinite(cell.Y) &&
            double.IsFinite(cell.Width) &&
            double.IsFinite(cell.Height) &&
            cell.Width >= 10 &&
            cell.Height >= 10 &&
            cell.Width <= 500 &&
            cell.Height <= 100
            ? new ScreenRect(cell.X, cell.Y, cell.Width, cell.Height)
            : null;
    }

    private static ScreenRect? TryGetFeishuBitableEditorAnchor(
        AutomationElement element,
        bool hasActiveEditorAncestor,
        bool hasTextPattern)
    {
        var current = element.Current;
        if (!AppProfileCatalog.SupportsWritableFeishuBitableSurface(
                current.ClassName,
                current.FrameworkId,
                current.ControlType,
                hasTextPattern,
                hasActiveEditorAncestor))
        {
            return null;
        }

        var bounds = current.BoundingRectangle;
        return double.IsFinite(bounds.X) &&
            double.IsFinite(bounds.Y) &&
            double.IsFinite(bounds.Width) &&
            double.IsFinite(bounds.Height) &&
            bounds.Width >= 20 &&
            bounds.Height >= 10
            ? new ScreenRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)
            : null;
    }

    private static bool HasBitableActiveEditorAncestor(AutomationElement element)
    {
        var ancestor = TreeWalker.ControlViewWalker.GetParent(element);
        for (var depth = 0; depth < 3 && ancestor is not null; depth++)
        {
            if (ancestor.Current.ClassName?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("bitable-text-editor-container--active", StringComparer.Ordinal) is true)
            {
                return true;
            }

            ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor);
        }

        return false;
    }

    private static bool HasFeishuChatParentShape(AutomationElement element)
    {
        var parent = TreeWalker.ControlViewWalker.GetParent(element);
        return parent is not null &&
            parent.Current.ControlType == ControlType.Group &&
            parent.Current.ClassName?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("outerdocbody", StringComparer.Ordinal) is true &&
            parent.Current.ClassName?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("editor-kit-outer-container", StringComparer.Ordinal) is true;
    }

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
            return CaretAnchorPolicy.KeepCaretLike(caret);
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

    private static ScreenRect? WindowBounds(nint window)
    {
        if (window == 0 || !NativeMethods.GetWindowRect(window, out var bounds))
        {
            return null;
        }

        var rectangle = new ScreenRect(
            bounds.Left,
            bounds.Top,
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top);
        return rectangle.IsUsable ? rectangle : null;
    }

    private static bool IsStillCurrent(ObservationIdentity initial)
    {
        return WindowsObservationIdentity.TryRead(out var current) &&
            ObservationValidator.IsCurrent(initial, current);
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

    private static ScreenRect? ToScreenRect(System.Windows.Rect source)
    {
        var rectangle = new ScreenRect(source.X, source.Y, source.Width, source.Height);
        return rectangle.IsUsable ? rectangle : null;
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
