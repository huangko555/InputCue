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
        var second = new InputCueSettings(
            true,
            1200,
            400,
            IndicatorPlacement.BottomLeft,
            HorizontalOffsetDip: -8,
            VerticalOffsetDip: 5,
            IndicatorSizeDip: 18,
            Style: IndicatorStyle.LightBadge,
            LightBadgeSizeDip: 44);

        Assert.True(store.TrySave(first));
        Assert.True(store.TrySave(second));

        Assert.Equal(second, store.Load());
        Assert.DoesNotContain("IsValid", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ExistingSettingsGainPositionDefaultsWithoutLosingTimingValues()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "IndicatorEnabled": false,
              "DisplayDurationMilliseconds": 725,
              "MinimumDisplayDurationMilliseconds": 225
            }
            """,
            Encoding.UTF8);
        var store = new InputCueSettingsStore(path);

        var settings = store.Load();

        Assert.False(settings.IndicatorEnabled);
        Assert.Equal(725, settings.DisplayDurationMilliseconds);
        Assert.Equal(225, settings.MinimumDisplayDurationMilliseconds);
        Assert.Equal(IndicatorPlacement.Right, settings.Placement);
        Assert.Equal(0, settings.HorizontalOffsetDip);
        Assert.Equal(0, settings.VerticalOffsetDip);
        Assert.Equal(InputCueSettings.DefaultIndicatorSizeDip, settings.IndicatorSizeDip);
        Assert.Equal(IndicatorStyle.Dot, settings.Style);
        Assert.Equal(InputCueSettings.DefaultLightBadgeSizeDip, settings.LightBadgeSizeDip);
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

    [Theory]
    [InlineData(IndicatorPlacement.Right, -41, 0, 12)]
    [InlineData(IndicatorPlacement.Right, 0, 41, 12)]
    [InlineData(IndicatorPlacement.Right, 0, 0, 5)]
    [InlineData(IndicatorPlacement.Right, 0, 0, 33)]
    [InlineData((IndicatorPlacement)99, 0, 0, 12)]
    public void InvalidPositionSettingsAreNotWritten(
        IndicatorPlacement placement,
        int horizontalOffsetDip,
        int verticalOffsetDip,
        int indicatorSizeDip)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new InputCueSettingsStore(path);

        var saved = store.TrySave(new InputCueSettings(
            true,
            1000,
            300,
            placement,
            horizontalOffsetDip,
            verticalOffsetDip,
            indicatorSizeDip));

        Assert.False(saved);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData((IndicatorStyle)99, 36)]
    [InlineData(IndicatorStyle.LightBadge, 23)]
    [InlineData(IndicatorStyle.LightBadge, 65)]
    public void InvalidStyleSettingsAreNotWritten(
        IndicatorStyle style,
        int lightBadgeSizeDip)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new InputCueSettingsStore(path);

        var saved = store.TrySave(new InputCueSettings(
            true,
            1000,
            300,
            Style: style,
            LightBadgeSizeDip: lightBadgeSizeDip));

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
