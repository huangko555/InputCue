using Microsoft.Win32;

namespace InputCue.Windows.Startup;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "InputCue";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string { Length: > 0 };
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static bool TrySetEnabled(bool enabled, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(
                    ValueName,
                    BuildCommand(executablePath),
                    RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static string BuildCommand(string executablePath) =>
        $"\"{Path.GetFullPath(executablePath)}\" --background";
}
