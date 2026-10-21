namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// The 500 ms guard (workbench-ui APR-04, ADR-0014 decision 6, SEC-24): each time a different question is shown, the
/// window becomes visible or the approval page is opened, neither answer button responds for 500 ms, counted from when
/// the new content has rendered, not from when it was asked for. A click meant for the last question never lands on
/// the next one (T21). Armed until the first rendering. Used on the UI thread; <see cref="Changed"/> may come from the
/// clock's timer thread.
/// </summary>
internal sealed class ResponseGuard(TimeProvider clock) : IDisposable
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    private readonly Lock _lock = new();
    private DateTimeOffset? _rendered;
    private ITimer? _timer;

    /// <summary>Whether the buttons may respond has changed.</summary>
    public event Action? Changed;

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                return _rendered is { } shown && clock.GetUtcNow() - shown >= Delay;
            }
        }
    }

    /// <summary>Something new is about to show: the buttons do not respond until it has rendered and 500 ms passed.</summary>
    public void Arm()
    {
        bool wasOpen = IsOpen;
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _rendered = null;
        }

        if (wasOpen)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>The new content has rendered: the 500 ms start now, once per <see cref="Arm"/>.</summary>
    public void Rendered()
    {
        lock (_lock)
        {
            if (_rendered is not null)
            {
                return;
            }

            _rendered = clock.GetUtcNow();
            _timer = clock.CreateTimer(_ => Changed?.Invoke(), null, Delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
