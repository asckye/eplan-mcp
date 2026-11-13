using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApprovalModalGuardTests
{
    private static readonly TimeSpan _second = TimeSpan.FromSeconds(1);

    [Fact]
    public void Exit_LetsAnOpenWindowRun()
    {
        Assert.Null(ApprovalModalGuard.Exit(windowAlive: true, closeAt: null, open: TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Exit_EndsTheLoopAtOnceWhenTheWindowIsGone()
    {
        Assert.Equal(ApprovalModalEnd.Destroyed, ApprovalModalGuard.Exit(windowAlive: false, closeAt: null, open: _second));
    }

    [Fact]
    public void Exit_GivesAClosedWindowTheGraceTimeBeforeEndingTheLoop()
    {
        Assert.Null(ApprovalModalGuard.Exit(true, _second, _second + ApprovalModalGuard.Grace - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(ApprovalModalEnd.Stuck, ApprovalModalGuard.Exit(true, _second, _second + ApprovalModalGuard.Grace));
    }

    [Fact]
    public void ToReenable_TakesOnlyWindowsEnabledBeforeThatStillExistAndAreDisabled()
    {
        IntPtr main = new(1), panel = new(2), gone = new(3), enabled = new(4);
        IntPtr[] before = [main, panel, gone, enabled];

        IReadOnlyList<IntPtr> result = ApprovalModalGuard.ToReenable(before, w => w != gone, w => w == enabled);

        Assert.Equal([main, panel], result);
    }
}
