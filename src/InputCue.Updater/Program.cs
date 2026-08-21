using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

namespace InputCue.Updater;

internal static class Program
{
    private const string PortableMarkerFileName = "portable.flag";
    private const string MainExecutableFileName = "InputCue.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = UpdateOptions.Parse(args);
            ApplyUpdate(options);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            TryWriteErrorLog(exception);
            return 1;
        }
    }

    private static void ApplyUpdate(UpdateOptions options)
    {
        ValidateInstallRoot(options.InstallRoot, options.RestartExecutable);
        WaitForProcessExit(options.WaitProcessId);

        var parentDirectory = Directory.GetParent(options.InstallRoot)?.FullName ??
            throw new InvalidOperationException("安装目录不能是磁盘根目录。");
        var operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var stagingDirectory = Path.Combine(parentDirectory, ".inputcue-staging-" + operationId);
        var backupDirectory = Path.Combine(parentDirectory, ".inputcue-backup-" + operationId);
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(backupDirectory);

        var installedEntries = new List<(string Destination, string Backup)>();
        var newEntries = new List<string>();
        try
        {
            ExtractSafely(options.PackagePath, stagingDirectory);
            ValidateStagingDirectory(stagingDirectory);

            foreach (var sourceEntry in Directory.EnumerateFileSystemEntries(stagingDirectory))
            {
                var name = Path.GetFileName(sourceEntry);
                if (string.Equals(name, "data", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("更新包不能包含 data 目录。");
                }

                var destination = Path.Combine(options.InstallRoot, name);
                var backup = Path.Combine(backupDirectory, name);
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    MoveEntry(destination, backup);
                    installedEntries.Add((destination, backup));
                }

                MoveEntry(sourceEntry, destination);
                newEntries.Add(destination);
            }

            var restartPath = Path.Combine(options.InstallRoot, Path.GetFileName(options.RestartExecutable));
            var startInfo = new ProcessStartInfo(restartPath)
            {
                UseShellExecute = true,
                WorkingDirectory = options.InstallRoot,
            };
            startInfo.ArgumentList.Add("--updated-from");
            startInfo.ArgumentList.Add(options.Version);
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("更新后无法重新启动 InputCue。");

            TryDeleteDirectory(backupDirectory);
            TryDeleteFile(options.PackagePath);
        }
        catch
        {
            RollBack(newEntries, installedEntries);
            TryRestartPreviousVersion(options);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
            TryDeleteDirectory(backupDirectory);
        }
    }

    private static void WaitForProcessExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                throw new InvalidOperationException("等待 InputCue 退出超时。");
            }
        }
        catch (ArgumentException)
        {
            // 主程序可能在更新器完成初始化前已经退出。
        }
    }

    private static void ExtractSafely(string packagePath, string stagingDirectory)
    {
        var stagingRoot = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包包含不安全的路径。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }
    }

    private static void ValidateInstallRoot(string installRoot, string restartExecutable)
    {
        var root = NormalizeDirectory(installRoot);
        var driveRoot = Path.GetPathRoot(root);
        if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), driveRoot?.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(root, PortableMarkerFileName)) ||
            !File.Exists(Path.Combine(root, MainExecutableFileName)) ||
            !string.Equals(NormalizeDirectory(Path.GetDirectoryName(Path.GetFullPath(restartExecutable))!), root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝更新未识别的安装目录。");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void ValidateStagingDirectory(string stagingDirectory)
    {
        if (!File.Exists(Path.Combine(stagingDirectory, PortableMarkerFileName)) ||
            !File.Exists(Path.Combine(stagingDirectory, MainExecutableFileName)) ||
            !File.Exists(Path.Combine(stagingDirectory, "InputCue.Updater.exe")))
        {
            throw new InvalidDataException("更新包缺少必要文件。");
        }
    }

    private static void RollBack(
        IEnumerable<string> newEntries,
        IEnumerable<(string Destination, string Backup)> installedEntries)
    {
        foreach (var entry in newEntries.Reverse())
        {
            TryDeleteEntry(entry);
        }

        foreach (var (destination, backup) in installedEntries.Reverse())
        {
            try
            {
                if (File.Exists(backup) || Directory.Exists(backup))
                {
                    MoveEntry(backup, destination);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TryRestartPreviousVersion(UpdateOptions options)
    {
        try
        {
            var path = Path.Combine(options.InstallRoot, Path.GetFileName(options.RestartExecutable));
            if (File.Exists(path))
            {
                _ = Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = options.InstallRoot,
                });
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void MoveEntry(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Move(source, destination);
        }
        else
        {
            Directory.Move(source, destination);
        }
    }

    private static void TryDeleteEntry(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryWriteErrorLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "InputCue");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "updater-error.log"),
                $"[{DateTimeOffset.Now:O}] {exception}\n");
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record UpdateOptions(
        int WaitProcessId,
        string InstallRoot,
        string PackagePath,
        string RestartExecutable,
        string Version)
    {
        public static UpdateOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("更新器参数无效。", nameof(args));
                }

                values.Add(args[index], args[index + 1]);
            }

            if (!values.TryGetValue("--wait-pid", out var processIdText) ||
                !int.TryParse(processIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
                processId <= 0 ||
                !values.TryGetValue("--install-root", out var installRoot) ||
                !values.TryGetValue("--package", out var packagePath) ||
                !values.TryGetValue("--restart-exe", out var restartExecutable) ||
                !values.TryGetValue("--version", out var version) ||
                string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("更新器参数不完整。", nameof(args));
            }

            return new UpdateOptions(
                processId,
                Path.GetFullPath(installRoot),
                Path.GetFullPath(packagePath),
                Path.GetFullPath(restartExecutable),
                version);
        }
    }
}
