namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// A clock that only moves when told to. <see cref="Advance"/> fires every timer that falls due on the way, in order
/// and on the calling thread, so a test sees exactly the ticks the time span holds.
/// </summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    /// <summary>The timers created so far, disposed ones included.</summary>
    public int TimersCreated { get; private set; }

    public DateTimeOffset Now
    {
        get
        {
            lock (_lock)
            {
                return _now;
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_lock)
        {
            TimersCreated++;
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset end = Now + by;
        while (true)
        {
            ManualTimer? next;
            lock (_lock)
            {
                next = _timers.Where(t => t.Due is { } due && due <= end).MinBy(t => t.Due);
                if (next is null)
                {
                    _now = end;
                    return;
                }

                _now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero ? _now + next.Period : null;
            }

            next.Fire();
        }
    }

    private sealed class ManualTimer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (time._lock)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : time._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (time._lock)
            {
                Due = null;
                time._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
