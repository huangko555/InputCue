using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace InputCue.Update;

public sealed class PortableUpdateManager : IDisposable
{
    public const string PortableMarkerFileName = "portable.flag";
    public const string UpdaterFileName = "InputCue.Updater.exe";
    public static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _baseDirectory;
    private readonly string _dataDirectory;
    private readonly string _statePath;
    private readonly Version _currentVersion;
    private readonly Uri _manifestUri;
    private readonly Uri _signatureUri;
    private readonly string _publicKeyPem;
    private bool _disposed;

    public PortableUpdateManager(
        string baseDirectory,
        string dataDirectory,
        Version currentVersion,
        Uri manifestUri,
        Uri signatureUri,
        HttpClient? httpClient = null,
        string? publicKeyPem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(manifestUri);
        ArgumentNullException.ThrowIfNull(signatureUri);

        _baseDirectory = Path.GetFullPath(baseDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _statePath = Path.Combine(_dataDirectory, "update-state.json");
        _currentVersion = currentVersion;
        _manifestUri = manifestUri;
        _signatureUri = signatureUri;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttpClient = httpClient is null;
        _publicKeyPem = publicKeyPem ?? ReadEmbeddedPublicKey();
    }

    public bool IsPortable =>
        File.Exists(Path.Combine(_baseDirectory, PortableMarkerFileName)) &&
        File.Exists(Path.Combine(_baseDirectory, UpdaterFileName));

    public TimeSpan GetAutomaticCheckDelay(DateTimeOffset now)
    {
        var state = LoadState();
        if (state?.LastCheckUtc is not { } lastCheck)
        {
            return TimeSpan.Zero;
        }

        var remaining = lastCheck + AutomaticCheckInterval - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public async Task<PortableUpdateResult> CheckAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsPortable)
            {
                return new PortableUpdateResult(
                    PortableUpdateStatus.Unsupported,
                    "当前不是便携发布包，自动更新不可用。");
            }

            var now = DateTimeOffset.UtcNow;
            var delay = GetAutomaticCheckDelay(now);
            if (!manual && delay > TimeSpan.Zero)
            {
                return new PortableUpdateResult(
                    PortableUpdateStatus.Deferred,
                    "今天已经检查过更新。");
            }

            // 在发起网络请求前落盘，网络故障也不会造成后台反复重试。
            SaveState(new PortableUpdateState(now));

            var manifestBytes = await _httpClient.GetByteArrayAsync(
                _manifestUri,
                cancellationToken).ConfigureAwait(false);
            var signatureText = await _httpClient.GetStringAsync(
                _signatureUri,
                cancellationToken).ConfigureAwait(false);
            if (!VerifyManifest(manifestBytes, signatureText))
            {
                return Failed("更新清单签名校验失败。");
            }

            var manifest = JsonSerializer.Deserialize<PortableUpdateManifest>(manifestBytes, JsonOptions);
            if (!TryValidateManifest(manifest, out var availableVersion, out var assetUri))
            {
                return Failed("更新清单格式无效。");
            }

            if (availableVersion <= _currentVersion)
            {
                return new PortableUpdateResult(
                    PortableUpdateStatus.UpToDate,
                    "当前已是最新版本。");
            }

            var packagePath = await DownloadPackageAsync(
                manifest!,
                availableVersion,
                assetUri!,
                cancellationToken).ConfigureAwait(false);
            return new PortableUpdateResult(
                PortableUpdateStatus.UpdateReady,
                $"已下载 v{FormatVersion(availableVersion)}，即将自动更新。",
                FormatVersion(availableVersion),
                packagePath);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or
            CryptographicException or FormatException or TaskCanceledException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return Failed($"检查更新失败，请稍后重试（{exception.GetType().Name}）。");
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool TryLaunchUpdater(PortableUpdateResult result, int processId, string restartExecutable)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (result is not { Status: PortableUpdateStatus.UpdateReady, PackagePath: not null } ||
            string.IsNullOrWhiteSpace(result.Version))
        {
            return false;
        }

        try
        {
            var sourceUpdater = Path.Combine(_baseDirectory, UpdaterFileName);
            var updaterTempRoot = Path.Combine(Path.GetTempPath(), "InputCue");
            TryCleanOldUpdaterCopies(updaterTempRoot);
            var launchDirectory = Path.Combine(
                updaterTempRoot,
                "updater-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(launchDirectory);
            var updaterPath = Path.Combine(launchDirectory, UpdaterFileName);
            File.Copy(sourceUpdater, updaterPath, overwrite: false);

            var startInfo = new ProcessStartInfo(updaterPath)
            {
                UseShellExecute = false,
                WorkingDirectory = launchDirectory,
            };
            startInfo.ArgumentList.Add("--wait-pid");
            startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--install-root");
            startInfo.ArgumentList.Add(_baseDirectory);
            startInfo.ArgumentList.Add("--package");
            startInfo.ArgumentList.Add(result.PackagePath);
            startInfo.ArgumentList.Add("--restart-exe");
            startInfo.ArgumentList.Add(Path.GetFullPath(restartExecutable));
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(result.Version);
            return Process.Start(startInfo) is not null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<string> DownloadPackageAsync(
        PortableUpdateManifest manifest,
        Version version,
        Uri assetUri,
        CancellationToken cancellationToken)
    {
        var updateDirectory = Path.Combine(_dataDirectory, "updates");
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(
            updateDirectory,
            $"InputCue-{FormatVersion(version)}-win-x64-portable.zip");
        var temporaryPath = packagePath + ".download";

        try
        {
            await using (var input = await _httpClient.GetStreamAsync(assetUri, cancellationToken)
                .ConfigureAwait(false))
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var fileInfo = new FileInfo(temporaryPath);
            if (fileInfo.Length != manifest.Asset.Size)
            {
                throw new InvalidDataException("The downloaded package size does not match.");
            }

            byte[] hash;
            await using (var packageStream = File.OpenRead(temporaryPath))
            {
                hash = await SHA256.HashDataAsync(packageStream, cancellationToken).ConfigureAwait(false);
            }

            var actualHash = Convert.ToHexString(hash);
            if (!string.Equals(actualHash, manifest.Asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException("The downloaded package hash does not match.");
            }

            File.Move(temporaryPath, packagePath, overwrite: true);
            return packagePath;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private bool VerifyManifest(byte[] manifestBytes, string signatureText)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(_publicKeyPem);
        var signature = Convert.FromBase64String(signatureText.Trim());
        return ecdsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256);
    }

    private static bool TryValidateManifest(
        PortableUpdateManifest? manifest,
        out Version version,
        out Uri? assetUri)
    {
        version = new Version();
        assetUri = null;
        if (manifest is not { SchemaVersion: 1, Asset.Size: > 0 } ||
            !Version.TryParse(manifest.Version, out var parsedVersion) ||
            parsedVersion.Build < 0 ||
            !Uri.TryCreate(manifest.Asset.Url, UriKind.Absolute, out var parsedAssetUri) ||
            parsedAssetUri.Scheme != Uri.UriSchemeHttps ||
            manifest.Asset.Sha256.Length != 64 ||
            !manifest.Asset.Sha256.All(Uri.IsHexDigit))
        {
            return false;
        }

        version = parsedVersion;
        assetUri = parsedAssetUri;
        return true;
    }

    private PortableUpdateState? LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return null;
            }

            using var stream = File.OpenRead(_statePath);
            return JsonSerializer.Deserialize<PortableUpdateState>(stream, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveState(PortableUpdateState state)
    {
        Directory.CreateDirectory(_dataDirectory);
        var temporaryPath = _statePath + ".tmp";
        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static string ReadEmbeddedPublicKey()
    {
        var assembly = typeof(PortableUpdateManager).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("update-public-key.pem", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException("The update public key is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static PortableUpdateResult Failed(string message) =>
        new(PortableUpdateStatus.Failed, message);

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{version.Build}";

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

    private static void TryCleanOldUpdaterCopies(string updaterTempRoot)
    {
        try
        {
            if (!Directory.Exists(updaterTempRoot))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         updaterTempRoot,
                         "updater-*",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record PortableUpdateState(DateTimeOffset LastCheckUtc);
}
