using System.Text.Json;

namespace InputCue.Core.Settings;

public sealed class InputCueSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public InputCueSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public InputCueSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return InputCueSettings.Default;
            }

            using var stream = File.OpenRead(_filePath);
            var settings = JsonSerializer.Deserialize<InputCueSettings>(stream, JsonOptions);
            return settings is { IsValid: true } ? settings : InputCueSettings.Default;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return InputCueSettings.Default;
        }
    }

    public bool TrySave(InputCueSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            return false;
        }

        var temporaryPath = _filePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
