using EplanMcp.Workbench.Approvals;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>The countdown (workbench-ui APR-10): the EPLAN dialog's format, the last 20 seconds, 30 and 10 seconds told.</summary>
[Trait("Size", "Small")]
public sealed class CountdownTests
{
    private readonly Question _question = new(Asks.Write(left: TimeSpan.FromSeconds(120)), 1, Asks.Now);

    [Theory]
    [InlineData(120_000, "2:00")]
    [InlineData(119_200, "2:00")]
    [InlineData(61_000, "1:01")]
    [InlineData(59_010, "1:00")]
    [InlineData(500, "0:01")]
    [InlineData(0, "0:00")]
    [InlineData(-5_000, "0:00")]
    public void TheTimeLeft_ReadsAsTheEplanDialogWritesIt(int milliseconds, string text)
    {
        Assert.Equal(text, Countdown.Clock(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void TheTimeLeft_GoesDownToZeroAndNoFurther_AndSoDoesTheBar()
    {
        Assert.Equal((TimeSpan.FromSeconds(120), 1d), (Countdown.Left(_question, Asks.Now), Countdown.Fraction(_question, Asks.Now)));
        Assert.Equal(0.5, Countdown.Fraction(_question, Asks.Now.AddSeconds(60)));
        Assert.Equal((TimeSpan.Zero, 0d), (Countdown.Left(_question, Asks.Now.AddMinutes(5)), Countdown.Fraction(_question, Asks.Now.AddMinutes(5))));
        Assert.Equal(0, Countdown.Fraction(new Question(Asks.Write(left: TimeSpan.Zero), 2, Asks.Now), Asks.Now));
    }

    [Theory]
    [InlineData(20_000, true)]
    [InlineData(20_001, false)]
    [InlineData(0, true)]
    public void TheLast20Seconds_AreAWarning(int milliseconds, bool warning)
    {
        Assert.Equal(warning, Countdown.IsWarning(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData(31_000, 29_000, 30)]
    [InlineData(30_500, 30_000, 30)]
    [InlineData(30_000, 29_000, null)]
    [InlineData(12_000, 9_000, 10)]
    [InlineData(40_000, 5_000, 10)]
    [InlineData(29_000, 11_000, null)]
    [InlineData(120_000, 119_000, null)]
    public void OnlyThirtyAndTenSeconds_AreAnnounced_OnceEach(int before, int after, int? announced)
    {
        Assert.Equal(announced, Countdown.Crossed(TimeSpan.FromMilliseconds(before), TimeSpan.FromMilliseconds(after)));
    }
}
