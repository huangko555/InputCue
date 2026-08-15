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

        Assert.True(EditableControlPolicy.SupportsTextEditing(controlType));
    }

    [Fact]
    public void SupportsTextEditingRejectsRadioButtonEvenWhenItHasWritableValue()
    {
        Assert.False(EditableControlPolicy.SupportsTextEditing(ControlType.RadioButton));
    }
}
