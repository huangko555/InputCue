using System.Text.Json;
using System.Text.Json.Nodes;
using InputCue.Core.Indicator;

namespace InputCue.Core.Settings;

public sealed class InputCueSettingsStore
{
    private const string LegacyLessDisplayKey = "LessDisplay";
    private const string SameAppPromptModeKey = nameof(InputCueSettings.SameAppPromptMode);

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
            var settings = DeserializeWithMigration(stream);
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

    /// <summary>
    /// Maps the legacy boolean <c>LessDisplay</c> key onto <see cref="InputCueSettings.SameAppPromptMode"/>
    /// when the file predates the three-mode setting. New-version files keep their explicit mode.
    /// </summary>
    private static InputCueSettings? DeserializeWithMigration(Stream stream)
    {
        var node = JsonNode.Parse(stream);
        if (node is JsonObject jsonObject &&
            jsonObject.ContainsKey(LegacyLessDisplayKey) &&
            !jsonObject.ContainsKey(SameAppPromptModeKey))
        {
            var suppressReplays =
                jsonObject[LegacyLessDisplayKey]?.GetValueKind() is JsonValueKind.True;
            jsonObject.Remove(LegacyLessDisplayKey);
            jsonObject[SameAppPromptModeKey] = (int)(suppressReplays
                ? AppStayPromptMode.Never
                : AppStayPromptMode.Always);
        }

        return node?.Deserialize<InputCueSettings>(JsonOptions);
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
