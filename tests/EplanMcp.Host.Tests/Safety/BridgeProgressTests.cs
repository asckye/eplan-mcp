using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// What the client sees while the Host waits for EPLAN (M4 design 4.3): a heartbeat every 5 s with the seconds waited,
/// the Bridge's steps as they come, and nothing once the call is over or cancelled.
/// </summary>
[Trait("Size", "Small")]
public sealed class BridgeProgressTests : IDisposable
{
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));
    private readonly RecordedProgress _client = new();
    private readonly CancellationTokenSource _cancel = new();

    public void Dispose() => _cancel.Dispose();

    [Fact]
    public void WhileEplanWorks_EveryFiveSecondsSaysForHowLongWithoutATotal()
    {
        using BridgeProgress waiting = Start();

        _time.Advance(TimeSpan.FromSeconds(16));

        Assert.Equal([5f, 10f, 15f], _client.Values.Select(v => v.Progress));
        Assert.All(_client.Values, v => Assert.Null(v.Total));
        Assert.Equal(["Updating reports (5 s)", "Updating reports (10 s)", "Updating reports (15 s)"], _client.Values.Select(v => v.Message));
    }

    [Fact]
    public void TheBridgesSteps_GoOutAtOnceAndTheHeartbeatThenRepeatsTheLast()
    {
        using BridgeProgress waiting = Start();

        _time.Advance(TimeSpan.FromSeconds(2));
        waiting.Report(Step("Exporting page 1 of 20"));
        _time.Advance(TimeSpan.FromSeconds(8)); // the heartbeat at 5 s comes right after the step and is left out

        Assert.Equal([2f, 10f], _client.Values.Select(v => v.Progress));
        Assert.Equal(["Exporting page 1 of 20", "Exporting page 1 of 20 (10 s)"], _client.Values.Select(v => v.Message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\u0007")]
    public void AStepWithoutText_SaysWhatIsWaitedFor(string? message)
    {
        using BridgeProgress waiting = Start();

        waiting.Report(Step(message));

        Assert.Equal("Updating reports", Assert.Single(_client.Values).Message);
    }

    [Fact]
    public void OnceTheClientCancelled_NothingIsReported()
    {
        using BridgeProgress waiting = Start();

        _cancel.Cancel();
        waiting.Report(Step("Exporting page 1 of 20"));
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.Empty(_client.Values);
    }

    [Fact]
    public void AfterDispose_NothingIsReported()
    {
        BridgeProgress waiting = Start();

        waiting.Dispose();
        waiting.Report(Step("Exporting page 1 of 20"));
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.Empty(_client.Values);
    }

    [Fact]
    public void WithoutAProgress_NoTimerRuns()
    {
        Assert.Null(BridgeProgress.Start(null, "Updating reports", _time, _cancel.Token));
        Assert.Equal(0, _time.TimersCreated);
    }

    private BridgeProgress Start() => BridgeProgress.Start(CallProgress.For(_client), "Updating reports", _time, _cancel.Token)!;

    private static ProgressNotification Step(string? message) => new() { RequestId = "r1", Progress = 0, Total = 20, Message = message };
}
