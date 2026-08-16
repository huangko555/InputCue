using System.Diagnostics;
using System.Runtime.InteropServices;
using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

internal static class TsfCaretProbe
{
    private static readonly Guid ClsidTfThreadMgr = new("529A9E6B-6587-4F23-AB9E-9C7D683E3C50");
    private static readonly Guid IidTfThreadMgr = new("AA80E801-2021-11D2-93E0-0060B067B86E");

    internal static TsfProbeResult Observe()
    {
        var startedAt = Stopwatch.GetTimestamp();
        var window = NativeMethods.GetForegroundWindow();
        uint processId = 0;
        var threadId = window == 0 ? 0u : NativeMethods.GetWindowThreadProcessId(window, out processId);
        if (window == 0)
        {
            return TsfProbeResult.Failure(window, 0, 0, "NoForegroundWindow", startedAt);
        }

        var initializeResult = NativeMethods.CoInitializeEx(0, NativeMethods.CoInitMultiThreaded);
        var shouldUninitialize = initializeResult is 0 or 1;
        try
        {
            if (initializeResult < 0 && initializeResult != NativeMethods.RpcChangedMode)
            {
                return TsfProbeResult.Failure(window, threadId, processId, "CoInitializeFailed", startedAt, initializeResult);
            }

            var createResult = NativeMethods.CoCreateInstance(
                ClsidTfThreadMgr,
                0,
                NativeMethods.ClassContextInprocServer,
                IidTfThreadMgr,
                out var unknown);
            if (createResult < 0 || unknown == 0)
            {
                return TsfProbeResult.Failure(window, threadId, processId, "ThreadManagerUnavailable", startedAt, createResult);
            }

            ITfThreadMgr? threadManager = null;
            try
            {
                threadManager = (ITfThreadMgr)Marshal.GetObjectForIUnknown(unknown);
                var activateResult = threadManager.Activate(out _);
                if (activateResult < 0)
                {
                    return TsfProbeResult.Failure(window, threadId, processId, "ActivateFailed", startedAt, activateResult);
                }

                try
                {
                    var focusResult = threadManager.GetFocus(out var documentManager);
                    if (focusResult < 0 || documentManager is null)
                    {
                        return TsfProbeResult.Failure(window, threadId, processId, "FocusContextUnavailable", startedAt, focusResult);
                    }

                    try
                    {
                        var topResult = documentManager.GetTop(out var context);
                        if (topResult < 0 || context is null)
                        {
                            return TsfProbeResult.Failure(window, threadId, processId, "TopContextUnavailable", startedAt, topResult);
                        }

                        try
                        {
                            var viewResult = context.GetActiveView(out var view);
                            if (viewResult < 0 || view is null)
                            {
                                return TsfProbeResult.Failure(window, threadId, processId, "ContextViewUnavailable", startedAt, viewResult);
                            }

                            try
                            {
                                // This is deliberately a document-start range probe, not a caret claim.
                                var rangeResult = context.GetStart(0, out var range);
                                if (rangeResult < 0 || range is null)
                                {
                                    return TsfProbeResult.Failure(window, threadId, processId, "RangeUnavailable", startedAt, rangeResult);
                                }

                                try
                                {
                                    var textExtResult = view.GetTextExt(0, range, out var rectangle, out var clipped);
                                    return TsfProbeResult.Success(
                                        window,
                                        threadId,
                                        processId,
                                        textExtResult,
                                        rectangle,
                                        clipped,
                                        startedAt);
                                }
                                finally
                                {
                                    Release(range);
                                }
                            }
                            finally
                            {
                                Release(view);
                            }
                        }
                        finally
                        {
                            Release(context);
                        }
                    }
                    finally
                    {
                        Release(documentManager);
                    }
                }
                finally
                {
                    _ = threadManager.Deactivate();
                }
            }
            finally
            {
                Release(threadManager);
                Marshal.Release(unknown);
            }
        }
        catch (COMException exception)
        {
            return TsfProbeResult.Failure(window, threadId, processId, "ComException", startedAt, exception.HResult);
        }
        catch (InvalidCastException)
        {
            return TsfProbeResult.Failure(window, threadId, processId, "InterfaceUnavailable", startedAt);
        }
        finally
        {
            if (shouldUninitialize)
            {
                NativeMethods.CoUninitialize();
            }
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}

internal sealed record TsfProbeResult(
    nint ForegroundWindow,
    uint ForegroundThreadId,
    uint ForegroundProcessId,
    string Status,
    int HResult,
    NativeRect? TextExtent,
    bool? Clipped,
    double DurationMilliseconds)
{
    internal static TsfProbeResult Failure(
        nint window,
        uint threadId,
        uint processId,
        string status,
        long startedAt,
        int hResult = 0) =>
        new(window, threadId, processId, status, hResult, null, null, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    internal static TsfProbeResult Success(
        nint window,
        uint threadId,
        uint processId,
        int hResult,
        NativeRect rectangle,
        bool clipped,
        long startedAt) =>
        new(window, threadId, processId, "TextExtentReturned", hResult, rectangle, clipped, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
}

[ComImport]
[Guid("AA80E801-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfThreadMgr
{
    int Activate(out int clientId);
    int Deactivate();
    int CreateDocumentMgr(out ITfDocumentMgr documentManager);
    int EnumDocumentMgrs(out object enumerator);
    int GetFocus(out ITfDocumentMgr documentManager);
}

[ComImport]
[Guid("AA80E7F4-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfDocumentMgr
{
    int CreateContext(int clientId, uint flags, object? textStore, out ITfContext context, out int editCookie);
    int Push(ITfContext context);
    int Pop(uint flags);
    int GetTop(out ITfContext context);
    int GetGlobalCompartment(out object compartment);
}

[ComImport]
[Guid("AA80E7FD-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfContext
{
    int RequestEditSession(int clientId, object editSession, uint flags, out int result);
    int InWriteSession(int clientId, out bool inWriteSession);
    int GetSelection(int editCookie, uint index, uint count, [Out] TfSelection[] selections, out uint fetched);
    int SetSelection(int editCookie, uint count, [In] TfSelection[] selections);
    int GetStart(int editCookie, out ITfRange range);
    int GetEnd(int editCookie, out ITfRange range);
    int GetActiveView(out ITfContextView view);
}

[ComImport]
[Guid("AA80E7FC-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfContextView
{
    int GetRangeFromPoint(nint point, out ITfRange range);
    int GetTextExt(int editCookie, ITfRange range, out NativeRect rectangle, out bool clipped);
    int GetScreenExt(out NativeRect rectangle);
}

[ComImport]
[Guid("AA80E7FF-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfRange
{
}

[StructLayout(LayoutKind.Sequential)]
internal struct TfSelection
{
    internal ITfRange Range;
    internal TfSelectionStyle Style;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TfSelectionStyle
{
    internal int Ase;
    internal int Incl;
}
