using System.Windows.Automation;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class EditableControlPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SupportsTextEditingAcceptsTextControls(bool document)
    {
        var controlType = document ? ControlType.Document : ControlType.Edit;

        Assert.True(EditableControlPolicy.SupportsTextEditing(controlType, hasValuePattern: false));
    }

    [Fact]
    public void SupportsTextEditingAcceptsWritableComboBox()
    {
        Assert.True(EditableControlPolicy.SupportsTextEditing(ControlType.ComboBox, hasValuePattern: true));
    }

    [Fact]
    public void SupportsTextEditingRejectsComboBoxWithoutValuePattern()
    {
        Assert.False(EditableControlPolicy.SupportsTextEditing(ControlType.ComboBox, hasValuePattern: false));
    }

    [Fact]
    public void SupportsTextEditingRejectsRadioButtonEvenWhenItHasWritableValue()
    {
        Assert.False(EditableControlPolicy.SupportsTextEditing(ControlType.RadioButton, hasValuePattern: true));
    }
}
