using System.Runtime.InteropServices;
using InputCue.Core.InputContext;
using Windows.Win32;
using Windows.Win32.UI.Accessibility;

namespace InputCue.Windows.InputContext;

internal sealed class NativeTextPattern2CaretProbe : IDisposable
{
    private readonly IUIAutomation _automation = (IUIAutomation)new CUIAutomation8();
    private bool _disposed;

    internal unsafe NativeCaretResult TryGetCaret()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IUIAutomationElement? element = null;
        object? patternObject = null;
        IUIAutomationTextRange? range = null;
        try
        {
            element = _automation.GetFocusedElement();
            var interfaceId = typeof(IUIAutomationTextPattern2).GUID;
            patternObject = element.GetCurrentPatternAs(
                UIA_PATTERN_ID.UIA_TextPattern2Id,
                in interfaceId);
            if (patternObject is not IUIAutomationTextPattern2 pattern)
            {
                return new NativeCaretResult(null, TextPattern2Status.PatternUnavailable);
            }

            range = pattern.GetCaretRange(out var isActive);
            if (!isActive)
            {
                return new NativeCaretResult(null, TextPattern2Status.Inactive);
            }

            var caret = ReadFirstRectangle(range.GetBoundingRectangles());
            return caret is null
                ? new NativeCaretResult(null, TextPattern2Status.EmptyBounds)
                : new NativeCaretResult(caret, TextPattern2Status.Succeeded);
        }
        catch (COMException)
        {
            return new NativeCaretResult(null, TextPattern2Status.ComError);
        }
        finally
        {
            ReleaseComObject(range);
            ReleaseComObject(patternObject);
            ReleaseComObject(element);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseComObject(_automation);
    }

    private static unsafe ScreenRect? ReadFirstRectangle(
        global::Windows.Win32.System.Com.SAFEARRAY* rectangles)
    {
        if (rectangles is null)
        {
            return null;
        }

        var accessed = false;
        try
        {
            PInvoke.SafeArrayGetLBound(rectangles, 1, out var lowerBound).ThrowOnFailure();
            PInvoke.SafeArrayGetUBound(rectangles, 1, out var upperBound).ThrowOnFailure();
            var count = upperBound - lowerBound + 1;
            if (count < 4)
            {
                return null;
            }

            PInvoke.SafeArrayAccessData(rectangles, out var data).ThrowOnFailure();
            accessed = true;
            var values = new ReadOnlySpan<double>(data, count);
            var width = values[2] == 0 ? 1 : values[2];
            var rectangle = new ScreenRect(values[0], values[1], width, values[3]);
            return rectangle.IsUsable ? rectangle : null;
        }
        finally
        {
            if (accessed)
            {
                _ = PInvoke.SafeArrayUnaccessData(rectangles);
            }

            _ = PInvoke.SafeArrayDestroy(rectangles);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.ReleaseComObject(value);
        }
    }

    internal sealed record NativeCaretResult(ScreenRect? Caret, TextPattern2Status Status);
}
