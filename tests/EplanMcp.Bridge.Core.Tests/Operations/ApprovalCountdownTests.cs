using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>The approval dialog's timing rules: the click guard, the countdown, and expiry before any choice.</summary>
[Trait("Size", "Small")]
public sealed class ApprovalCountdownTests
{
    private readonly ApprovalCountdown _countdown = new(TimeSpan.FromMinutes(5));

    [Fact]
    public void ClicksInTheFirst500Ms_AreIgnored_ClosingIsNot()
    {
        TimeSpan early = TimeSpan.FromMilliseconds(499);

        Assert.False(_countdown.AcceptsClicks(early));
        Assert.Null(_countdown.Decide(ApprovalChoice.Approve, early));
        Assert.Null(_countdown.Decide(ApprovalChoice.Reject, early));
        Assert.Equal(ApprovalDecisions.Cancelled, _countdown.Decide(ApprovalChoice.Close, early));
        Assert.Equal(TimeSpan.FromMilliseconds(500), ApprovalCountdown.ClickGuard);
    }

    [Fact]
    public void AfterTheGuard_EachChoiceHasItsDecision()
    {
        TimeSpan later = TimeSpan.FromSeconds(3);

        Assert.True(_countdown.AcceptsClicks(later));
        Assert.Equal(ApprovalDecisions.Granted, _countdown.Decide(ApprovalChoice.Approve, later));
        Assert.Equal(ApprovalDecisions.Denied, _countdown.Decide(ApprovalChoice.Reject, later));
        Assert.Equal(ApprovalDecisions.Cancelled, _countdown.Decide(ApprovalChoice.Close, later));
    }

    [Fact]
    public void AtTheDeadline_EverythingIsExpired()
    {
        TimeSpan deadline = TimeSpan.FromMinutes(5);

        Assert.True(_countdown.HasExpired(deadline));
        Assert.False(_countdown.AcceptsClicks(deadline));
        Assert.All(Enum.GetValues<ApprovalChoice>(), c => Assert.Equal(ApprovalDecisions.Expired, _countdown.Decide(c, deadline)));
        Assert.False(_countdown.HasExpired(deadline - TimeSpan.FromMilliseconds(1)));
    }

    [Theory]
    [InlineData(0, "5:00")]
    [InlineData(1, "5:00")]
    [InlineData(1000, "4:59")]
    [InlineData(299_001, "0:01")]
    [InlineData(300_000, "0:00")]
    [InlineData(400_000, "0:00")]
    public void Remaining_IsMinutesAndSecondsRoundedUp(int elapsedMs, string shown)
    {
        Assert.Equal(shown, _countdown.Remaining(TimeSpan.FromMilliseconds(elapsedMs)));
    }
}
