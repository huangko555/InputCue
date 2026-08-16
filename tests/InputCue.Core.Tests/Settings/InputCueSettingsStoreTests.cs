using System.Text;
using InputCue.Core.Settings;

namespace InputCue.Core.Tests.Settings;

public sealed class InputCueSettingsStoreTests
{
    [Fact]
    public void MissingFileLoadsDefaults()
    {
        using var directory = new TemporaryDirectory();
        var store = new InputCueSettingsStore(Path.Combine(directory.Path, "settings.json"));

        var settings = store.Load();

        Assert.Equal(InputCueSettings.Default, settings);
    }

    [Fact]
    public void SavedSettingsRoundTripAndReplacePreviousValues()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new InputCueSettingsStore(path);
        var first = new InputCueSettings(false, 800, 250);
        var second = new InputCueSettings(true, 1200, 400);

        Assert.True(store.TrySave(first));
        Assert.True(store.TrySave(second));

        Assert.Equal(second, store.Load());
        Assert.DoesNotContain("IsValid", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"IndicatorEnabled\":true,\"DisplayDurationMilliseconds\":-1,\"MinimumDisplayDurationMilliseconds\":300}")]
    public void DamagedOrInvalidFileLoadsDefaults(string content)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, content, Encoding.UTF8);
        var store = new InputCueSettingsStore(path);

        var settings = store.Load();

        Assert.Equal(InputCueSettings.Default, settings);
    }

    [Fact]
    public void InvalidSettingsAreNotWritten()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new InputCueSettingsStore(path);

        var saved = store.TrySave(new InputCueSettings(true, 60001, 300));

        Assert.False(saved);
        Assert.False(File.Exists(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"InputCue.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
