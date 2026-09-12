namespace InputCue.Core.Indicator;

/// <summary>Controls whether the indicator behaves as an idle readiness cue or a transient event cue.</summary>
public enum IndicatorDisplayMode
{
    /// <summary>Stay visible while input is idle, hide during editing, and return after the idle delay.</summary>
    IdlePersistent,

    /// <summary>Show briefly when an input context is established or the input state changes.</summary>
    Transient,
}
