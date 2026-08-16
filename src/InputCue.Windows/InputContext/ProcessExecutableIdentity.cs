using System.ComponentModel;
using System.Diagnostics;

namespace InputCue.Windows.InputContext;

internal sealed class ProcessExecutableIdentity
{
    private readonly Func<int, string?> _readExecutablePath;

    internal ProcessExecutableIdentity()
        : this(ReadExecutablePath)
    {
    }

    internal ProcessExecutableIdentity(Func<int, string?> readExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(readExecutablePath);
        _readExecutablePath = readExecutablePath;
    }

    internal bool IsCompatible(uint foregroundProcessId, int focusedProcessId)
    {
        if (foregroundProcessId <= 0 || focusedProcessId <= 0)
        {
            return false;
        }

        if (foregroundProcessId == (uint)focusedProcessId)
        {
            return true;
        }

        if (foregroundProcessId > int.MaxValue)
        {
            return false;
        }

        var foregroundPath = _readExecutablePath((int)foregroundProcessId);
        var focusedPath = _readExecutablePath(focusedProcessId);
        return !string.IsNullOrWhiteSpace(foregroundPath) &&
            !string.IsNullOrWhiteSpace(focusedPath) &&
            string.Equals(foregroundPath, focusedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadExecutablePath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            NotSupportedException or
            Win32Exception)
        {
            return null;
        }
    }
}
