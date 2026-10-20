using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The approval dialog's timing rules: the click guard counts from when the dialog appeared, the deadline from when it
/// was opened, so it expires even if it never appears; and the countdown.
/// </summary>
[Trait("Size", "Small")]
public sealed class ApprovalCountdownTests
{
    private static readonly TimeSpan _deadline = TimeSpan.FromMinutes(5);

    private readonly ApprovalCountdown _countdown = new(_deadline);

    [Fact]
    public void ClicksInTheFirst500MsOnScreen_AreIgnored_ClosingIsNot()
    {
        TimeSpan open = TimeSpan.FromSeconds(10);
        TimeSpan early = TimeSpan.FromMilliseconds(499);

        Assert.False(_countdown.AcceptsClicks(open, early));
        Assert.Null(_countdown.Decide(ApprovalChoice.Approve, open, early));
        Assert.Null(_countdown.Decide(ApprovalChoice.Reject, open, early));
        Assert.Equal(ApprovalDecisions.Cancelled, _countdown.Decide(ApprovalChoice.Close, open, early));
        Assert.Equal(TimeSpan.FromMilliseconds(500), ApprovalCountdown.ClickGuard);
    }

    [Fact]
    public void BeforeTheDialogAppears_NoClickCounts()
    {
        Assert.False(_countdown.AcceptsClicks(TimeSpan.FromMinutes(1), null));
        Assert.Null(_countdown.Decide(ApprovalChoice.Approve, TimeSpan.FromMinutes(1), null));
    }

    [Fact]
    public void AfterTheGuard_EachChoiceHasItsDecision()
    {
        TimeSpan open = TimeSpan.FromSeconds(3);
        TimeSpan shown = TimeSpan.FromSeconds(2);

        Assert.True(_countdown.AcceptsClicks(open, shown));
        Assert.Equal(ApprovalDecisions.Granted, _countdown.Decide(ApprovalChoice.Approve, open, shown));
        Assert.Equal(ApprovalDecisions.Denied, _countdown.Decide(ApprovalChoice.Reject, open, shown));
        Assert.Equal(ApprovalDecisions.Cancelled, _countdown.Decide(ApprovalChoice.Close, open, shown));
    }

    [Fact]
    public void AtTheDeadline_EverythingIsExpired_EvenIfTheDialogNeverAppeared()
    {
        Assert.True(_countdown.HasExpired(_deadline));
        Assert.False(_countdown.AcceptsClicks(_deadline, _deadline));
        Assert.All(Enum.GetValues<ApprovalChoice>(), c => Assert.Equal(ApprovalDecisions.Expired, _countdown.Decide(c, _deadline, _deadline)));
        Assert.Equal(ApprovalDecisions.Expired, _countdown.Decide(ApprovalChoice.Close, _deadline, null));
        Assert.False(_countdown.HasExpired(_deadline - TimeSpan.FromMilliseconds(1)));
    }

    [Theory]
    [InlineData(0, "5:00")]
    [InlineData(1, "5:00")]
    [InlineData(1000, "4:59")]
    [InlineData(299_001, "0:01")]
    [InlineData(300_000, "0:00")]
    [InlineData(400_000, "0:00")]
    public void Remaining_IsMinutesAndSecondsRoundedUp(int openMs, string shown)
    {
        Assert.Equal(shown, _countdown.Remaining(TimeSpan.FromMilliseconds(openMs)));
    }
}
