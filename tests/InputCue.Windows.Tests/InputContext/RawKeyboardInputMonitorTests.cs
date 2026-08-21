using System.Windows;
using InputCue.Windows.InputContext;

namespace InputCue.Windows.Tests.InputContext;

public sealed class RawKeyboardInputMonitorTests
{
    [Fact]
    public void AttachCreatesHandleForWindowThatHasNotBeenShown()
    {
        Exception? failure = null;
        var attached = false;
        var thread = new Thread(() =>
        {
            using var monitor = new RawKeyboardInputMonitor();
            var window = new Window();
            try
            {
                attached = monitor.Attach(window);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();

        Assert.True(
            thread.Join(TimeSpan.FromSeconds(15)),
            "The STA window thread did not complete within the CI scheduling budget.");
        Assert.Null(failure);
        Assert.True(attached);
    }
}
