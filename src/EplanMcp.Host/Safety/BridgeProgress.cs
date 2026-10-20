using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol;

namespace EplanMcp.Host.Safety;

/// <summary>
/// Tells the client how a wait for EPLAN goes (M4 design 4.3). Progress is the seconds waited, with no total, so it keeps
/// increasing whatever reports: a heartbeat every <see cref="Interval"/> ("Updating reports (40 s)"), and each step the
/// Bridge reports, at once, with the Bridge's text ("Exporting page 3 of 20"). A heartbeat is left out while a step has
/// just been reported, and repeats the step's text otherwise. Nothing is reported once the client has cancelled the
/// call, nor after <see cref="Dispose"/> returns, so none follows the tool's result. Thread-safe: the timer and the
/// pipe's reading thread report under <see cref="_lock"/>, which guards the mutable fields.
/// </summary>
internal sealed class BridgeProgress : IProgress<ProgressNotification>, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private readonly IProgress<ProgressNotificationValue> _lane;
    private readonly string _activity;
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _started;
    private readonly ITimer _timer;
    private readonly CancellationTokenRegistration _cancelled;
    private string? _step;
    private DateTimeOffset _stepAt;
    private bool _stopped;

    private BridgeProgress(IProgress<ProgressNotificationValue> lane, string activity, TimeProvider clock, CancellationToken cancellationToken)
    {
        _lane = lane;
        _activity = activity;
        _clock = clock;
        _started = clock.GetUtcNow();
        _timer = clock.CreateTimer(_ => Heartbeat(), null, Interval, Interval);
        _cancelled = cancellationToken.Register(Stop);
    }

    /// <summary>
    /// Starts reporting the wait for <paramref name="activity"/>, a short English phrase such as "Updating reports", in a
    /// new lane of <paramref name="call"/>; null without a progress to report to.
    /// </summary>
    public static BridgeProgress? Start(CallProgress? call, string activity, TimeProvider clock, CancellationToken cancellationToken) =>
        call is null ? null : new BridgeProgress(call.Lane(), activity, clock, cancellationToken);

    /// <summary>A step the Bridge reported, on the pipe's reading thread.</summary>
    public void Report(ProgressNotification value)
    {
        lock (_lock)
        {
            if (_stopped)
            {
                return;
            }

            // The Bridge sends no project content here, but its text reaches the client unshaped otherwise (architecture 8.5).
            _step = ResultShaper.CleanText(value.Message) is { Length: > 0 } text ? text : _activity;
            _stepAt = _clock.GetUtcNow();
            _lane.Report(new ProgressNotificationValue { Progress = Waited(_stepAt), Message = _step });
        }
    }

    public void Dispose()
    {
        Stop();
        _cancelled.Dispose();
    }

    private void Heartbeat()
    {
        lock (_lock)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            if (_stopped || (_step is not null && now - _stepAt < Interval))
            {
                return;
            }

            float seconds = Waited(now);
            string whole = ((long)Math.Round(seconds)).ToString(CultureInfo.InvariantCulture);
            _lane.Report(new ProgressNotificationValue { Progress = seconds, Message = $"{_step ?? _activity} ({whole} s)" });
        }
    }

    private float Waited(DateTimeOffset now) => (float)(now - _started).TotalSeconds;

    private void Stop()
    {
        lock (_lock)
        {
            _stopped = true;
        }

        _timer.Dispose();
    }
}
