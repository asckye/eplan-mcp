using EplanMcp.Workbench.Shell;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>One Workbench per user and data root (ADR-0014 decision 1): a second start finds the first and wakes it.</summary>
[Trait("Size", "Medium")]
public sealed class SingleInstanceTests
{
    private readonly string _name = "EplanMcp.Workbench.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void TheFirstStart_IsFirst_ASecondIsNot_AndOnceTheFirstEndsTheNextIsFirstAgain()
    {
        using (var first = new SingleInstance(_name))
        {
            using var second = new SingleInstance(_name);
            Assert.True(first.IsFirst);
            Assert.False(second.IsFirst);
        }

        using var next = new SingleInstance(_name);
        Assert.True(next.IsFirst);
    }

    [Fact]
    public void ASecondStart_WakesTheFirst_EachTime()
    {
        // The semaphore comes first, so it is disposed after the listener has stopped: a wake that reached a disposed
        // semaphore would end the test process (seen on CI).
        using var woken = new SemaphoreSlim(0);
        using var first = new SingleInstance(_name);
        first.Listen(() => woken.Release());

        for (int i = 0; i < 2; i++)
        {
            using var second = new SingleInstance(_name);
            second.SignalFirst();
            Assert.True(woken.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), $"start {i + 2} did not wake the first");
        }
    }

    [Fact]
    public void AnotherDataRoot_RunsItsOwnWorkbench()
    {
        using var one = new SingleInstance(_name);
        using var other = new SingleInstance(_name + ".other");

        Assert.True(one.IsFirst);
        Assert.True(other.IsFirst);
    }
}
