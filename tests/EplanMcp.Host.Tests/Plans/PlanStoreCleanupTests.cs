using System.Threading.Channels;
using EplanMcp.Host.Plans;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanStoreCleanupTests
{
    [Fact]
    public async Task HourlyWorker_UsesInjectedClock_AndDoesNotNeedAReadOrCreation()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        var clock = new ManualClock(f.Definition.ExpiresAt.AddDays(30).AddHours(-1));
        var store = new PlanStore(f.Locations, f.Audit) { Clock = clock };
        using var worker = new PlanStoreCleanup(store, NullLogger<PlanStoreCleanup>.Instance);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        await worker.StartAsync(deadline.Token);
        ManualTimer first = await clock.Timers.Reader.ReadAsync(deadline.Token);
        Assert.Equal(TimeSpan.FromHours(1), first.DueTime);
        Assert.True(File.Exists(f.Record));
        clock.Now = clock.Now.AddHours(1);
        first.Fire();
        _ = await clock.Timers.Reader.ReadAsync(deadline.Token); // The next delay starts after cleanup finished.
        Assert.False(File.Exists(f.Record));
        await worker.StopAsync(deadline.Token);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public Channel<ManualTimer> Timers { get; } = Channel.CreateUnbounded<ManualTimer>();
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            Assert.True(Timers.Writer.TryWrite(timer));
            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        public TimeSpan DueTime { get; } = dueTime;
        public void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { /* No native timer is allocated by this fake. */ }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
