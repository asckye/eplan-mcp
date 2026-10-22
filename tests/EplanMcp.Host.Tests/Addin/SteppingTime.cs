namespace EplanMcp.Host.Tests.Addin;

/// <summary>
/// A clock for code that only waits and measures what it waited: a delay or timer completes at once, and the steady
/// clock (<see cref="TimeProvider.GetTimestamp"/>) is then that much later, so a poll loop with a budget runs exactly as
/// many rounds as the budget holds, whatever the machine is doing. The time of day (<see cref="GetUtcNow"/>) stands
/// still, for records that carry it. One waiter at a time, as install-addin has; timers fire once.
/// </summary>
internal sealed class SteppingTime(DateTimeOffset now) : TimeProvider
{
    private long _ticks;

    public DateTimeOffset Now => now;

    public override DateTimeOffset GetUtcNow() => now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (period != Timeout.InfiniteTimeSpan && period != TimeSpan.Zero)
        {
            throw new NotSupportedException("Periodic timers are not stepped.");
        }

        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            Interlocked.Add(ref _ticks, dueTime.Ticks);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new Fired();
    }

    private sealed class Fired : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
