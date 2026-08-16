using System.Windows.Automation;

namespace InputCue.Windows.InputContext;

internal static class EditableControlPolicy
{
    internal static bool SupportsTextEditing(
        ControlType? controlType,
        bool hasValuePattern) =>
        controlType == ControlType.Edit ||
        controlType == ControlType.Document ||
        controlType == ControlType.ComboBox && hasValuePattern;
}
