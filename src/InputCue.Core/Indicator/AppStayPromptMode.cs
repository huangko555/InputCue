namespace InputCue.Core.Indicator;

/// <summary>
/// Controls whether context-established prompts replay while the user stays inside one application.
/// </summary>
public enum AppStayPromptMode
{
    /// <summary>Every prompt opportunity within the stay shows the indicator again.</summary>
    Always,

    /// <summary>Prompt opportunities replay only after the configured delay since the last prompt display.</summary>
    AfterDelay,

    /// <summary>Only the first opportunity in a stay shows the indicator; later context switches stay silent.</summary>
    Never,
}
