using InputCue.Windows.SingleInstance;

namespace InputCue.Windows.Tests.SingleInstance;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void SecondAcquisitionSignalsTheExistingInstance()
    {
        var instanceName = $"InputCue.Tests.{Guid.NewGuid():N}";
        using var activationRequested = new ManualResetEventSlim();
        using var first = SingleInstanceCoordinator.TryAcquire(
            instanceName,
            activationRequested.Set);

        using var second = SingleInstanceCoordinator.TryAcquire(instanceName, () => { });

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.True(activationRequested.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void InstanceCanBeReacquiredAfterTheOwnerIsDisposed()
    {
        var instanceName = $"InputCue.Tests.{Guid.NewGuid():N}";
        var first = SingleInstanceCoordinator.TryAcquire(instanceName, () => { });
        Assert.NotNull(first);
        first.Dispose();

        using var next = SingleInstanceCoordinator.TryAcquire(instanceName, () => { });

        Assert.NotNull(next);
    }
}
