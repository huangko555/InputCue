using System.Runtime.InteropServices;
using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

internal static class ProcessAncestry
{
    private const uint SnapshotProcesses = 0x00000002;
    private const int MaximumAncestorDepth = 8;
    private static readonly nint InvalidHandleValue = new(-1);

    internal static bool IsDescendantOf(int processId, int ancestorProcessId)
    {
        if (processId <= 0 || ancestorProcessId <= 0 || processId == ancestorProcessId)
        {
            return false;
        }

        var snapshot = NativeMethods.CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue)
        {
            return false;
        }

        try
        {
            var parents = ReadParents(snapshot);
            var current = processId;
            for (var depth = 0; depth < MaximumAncestorDepth; depth++)
            {
                if (!parents.TryGetValue(current, out var parent) ||
                    parent <= 0 ||
                    parent == current)
                {
                    return false;
                }

                if (parent == ancestorProcessId)
                {
                    return true;
                }

                current = parent;
            }

            return false;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(snapshot);
        }
    }

    private static Dictionary<int, int> ReadParents(nint snapshot)
    {
        var parents = new Dictionary<int, int>();
        var entry = ProcessEntry32.Create();
        if (!NativeMethods.Process32First(snapshot, ref entry))
        {
            return parents;
        }

        do
        {
            if (entry.ProcessId <= int.MaxValue && entry.ParentProcessId <= int.MaxValue)
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }

            entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
        }
        while (NativeMethods.Process32Next(snapshot, ref entry));

        return parents;
    }
}
