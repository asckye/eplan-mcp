using EplanMcp.Host.Safety;
using ModelContextProtocol;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// One tool call waits several times, each counting from zero, yet MCP wants every progress report to increase on the
/// last one; each wait's lane continues where the client's progress stands.
/// </summary>
[Trait("Size", "Small")]
public sealed class CallProgressTests
{
    private readonly RecordedProgress _client = new();

    [Fact]
    public void TheFirstWait_IsPassedOnAsItIs()
    {
        IProgress<ProgressNotificationValue> approval = CallProgress.For(_client)!.Lane();

        approval.Report(new ProgressNotificationValue { Progress = 15, Total = 120, Message = "a" });
        approval.Report(new ProgressNotificationValue { Progress = 30, Total = 120, Message = "b" });

        Assert.Equal([(15f, 120f, "a"), (30f, 120f, "b")], Seen());
    }

    [Fact]
    public void ALaterWait_ContinuesPastTheLastReportTotalIncluded()
    {
        CallProgress call = CallProgress.For(_client)!;
        IProgress<ProgressNotificationValue> confirmation = call.Lane();
        IProgress<ProgressNotificationValue> approval = call.Lane();
        IProgress<ProgressNotificationValue> eplan = call.Lane();

        confirmation.Report(new ProgressNotificationValue { Progress = 15, Total = 120 });
        approval.Report(new ProgressNotificationValue { Progress = 15, Total = 120 });
        approval.Report(new ProgressNotificationValue { Progress = 30, Total = 120 });
        eplan.Report(new ProgressNotificationValue { Progress = 5 });

        Assert.Equal([(15f, 120f, null), (31f, 136f, null), (46f, 136f, null), (52f, null, null)], Seen());
    }

    [Fact]
    public void AReportThatWouldNotIncrease_IsDropped()
    {
        IProgress<ProgressNotificationValue> lane = CallProgress.For(_client)!.Lane();

        lane.Report(new ProgressNotificationValue { Progress = 5 });
        lane.Report(new ProgressNotificationValue { Progress = 5 });
        lane.Report(new ProgressNotificationValue { Progress = 4 });

        Assert.Equal([5f], _client.Values.Select(v => v.Progress));
    }

    [Fact]
    public void WithoutAProgress_ThereIsNone() => Assert.Null(CallProgress.For(null));

    private List<(float Progress, float? Total, string? Message)> Seen() => [.. _client.Values.Select(v => (v.Progress, v.Total, v.Message))];
}
