using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// The 500 ms guard (workbench-ui APR-04, SEC-24, T21): closed from the moment something new is to show until 500 ms
/// after it rendered; a new arming always starts over, and a stale timer never opens it.
/// </summary>
[Trait("Size", "Small")]
public sealed class ResponseGuardTests : IDisposable
{
    private readonly ManualTime _clock = new(Asks.Now);
    private readonly ResponseGuard _guard;
    private int _changes;

    public ResponseGuardTests()
    {
        _guard = new ResponseGuard(_clock);
        _guard.Changed += () => _changes++;
    }

    public void Dispose() => _guard.Dispose();

    [Fact]
    public void ItStaysClosedUntilTheContentRendered_AndOpens500msLater()
    {
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(_guard.IsOpen);

        _guard.Rendered();
        _clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.False(_guard.IsOpen);
        _clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.True(_guard.IsOpen);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void ArmingAgain_ClosesItAtOnce_AndTheCountStartsOverFromTheNextRendering()
    {
        Open();

        _guard.Arm();
        Assert.False(_guard.IsOpen);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(_guard.IsOpen);
        _guard.Rendered();
        _clock.Advance(TimeSpan.FromMilliseconds(200));

        Assert.False(_guard.IsOpen); // a click 200 ms after the next question showed lands nowhere
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.True(_guard.IsOpen);
    }

    [Fact]
    public void ArmingWhileCounting_KeepsTheEarlierTimerFromOpeningIt()
    {
        _guard.Rendered();
        _clock.Advance(TimeSpan.FromMilliseconds(400));

        _guard.Arm();
        _clock.Advance(TimeSpan.FromMilliseconds(200));

        Assert.False(_guard.IsOpen);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void RenderingAgain_DoesNotPushTheOpeningBack()
    {
        _guard.Rendered();
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        _guard.Rendered();
        _clock.Advance(TimeSpan.FromMilliseconds(200));

        Assert.True(_guard.IsOpen);
    }

    [Fact]
    public void SettingTheWallClock_NeitherOpensItEarly_NorClosesIt()
    {
        var time = new SteppedTime(_clock);
        using var guard = new ResponseGuard(time);

        guard.Rendered();
        time.Step(TimeSpan.FromHours(1)); // set forward by hand or by time sync: no 500 ms have passed
        Assert.False(guard.IsOpen);
        _clock.Advance(ResponseGuard.Delay);
        Assert.True(guard.IsOpen);
        time.Step(TimeSpan.FromDays(-1));

        Assert.True(guard.IsOpen);
    }

    private void Open()
    {
        _guard.Rendered();
        _clock.Advance(ResponseGuard.Delay);
        Assert.True(_guard.IsOpen);
    }

    /// <summary>A clock whose wall time can be set, as the engineer or time sync does, while its steady time runs on.</summary>
    private sealed class SteppedTime(ManualTime steady) : TimeProvider
    {
        private TimeSpan _step;

        public override long TimestampFrequency => steady.TimestampFrequency;

        public void Step(TimeSpan by) => _step += by;

        public override DateTimeOffset GetUtcNow() => steady.GetUtcNow() + _step;

        public override long GetTimestamp() => steady.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            steady.CreateTimer(callback, state, dueTime, period);
    }
}
