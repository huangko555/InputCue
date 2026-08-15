using System.Text.Json;
using System.Text.Json.Serialization;
using InputCue.Core.Indicator;
using InputCue.Core.InputContext;

namespace InputCue.Core.Tests.Indicator;

public sealed class IndicatorGoldenTraceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData("wpf-native-basic.json", "VHHH")]
    [InlineData("edge-basic.json", "VHHH")]
    [InlineData("chrome-basic.json", "VHHH")]
    [InlineData("system-dialog-basic.json", "VVHH")]
    [InlineData("rapid-focus-switch-basic.json", "VVVVVV")]
    public void GoldenTraceProducesExpectedIndicatorPhases(
        string fileName,
        string expectedPhases)
    {
        var states = Replay(fileName);

        var phases = string.Concat(states.Select(ToSymbol));
        Assert.Equal(expectedPhases, phases);
    }

    private static IndicatorViewState[] Replay(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "traces", fileName);
        var json = File.ReadAllText(path);
        var trace = Assert.IsType<InputContextTrace>(
            JsonSerializer.Deserialize<InputContextTrace>(json, JsonOptions));
        var snapshots = InputContextTraceReplay.Reclassify(trace);
        var session = new IndicatorSession(
            IndicatorSessionOptions.Default with { AlwaysVisible = true });

        return snapshots
            .Select(snapshot => snapshot with { InputState = InputState.English })
            .Select(session.Observe)
            .ToArray();
    }

    private static char ToSymbol(IndicatorViewState state) =>
        state.Phase is IndicatorPhase.Visible ? 'V' : 'H';
}
