using System.Globalization;
using ModelContextProtocol;

namespace EplanMcp.Host.Exports;

/// <summary>
/// Tells the client, while the Host waits for EPLAN to export, that the export is still running (M4 design 4.3; tool
/// specs, section 5): every <see cref="Interval"/>, the seconds so far as progress, without a total, e.g.
/// "Exporting PDF (40 s)". The reports reach the client only when it sent a progress token: the SDK drops them
/// otherwise. No report is made after <see cref="Dispose"/> returns, so none follows the tool's result.
/// </summary>
internal sealed class ExportHeartbeat : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private readonly IProgress<ProgressNotificationValue> _progress;
    private readonly string _label;
    private readonly DateTimeOffset _started;
    private readonly TimeProvider _clock;
    private readonly ITimer _timer;
    private bool _stopped;

    private ExportHeartbeat(IProgress<ProgressNotificationValue> progress, string label, TimeProvider clock)
    {
        _progress = progress;
        _label = label;
        _clock = clock;
        _started = clock.GetUtcNow();
        _timer = clock.CreateTimer(_ => Report(), null, Interval, Interval);
    }

    /// <summary>Starts reporting as <paramref name="label"/>, a short English phrase; null without a progress to report to.</summary>
    public static ExportHeartbeat? Start(IProgress<ProgressNotificationValue>? progress, string label, TimeProvider clock) =>
        progress is null ? null : new ExportHeartbeat(progress, label, clock);

    public void Dispose()
    {
        lock (_lock)
        {
            _stopped = true;
        }

        _timer.Dispose();
    }

    private void Report()
    {
        lock (_lock)
        {
            if (_stopped)
            {
                return;
            }

            long seconds = (long)Math.Round((_clock.GetUtcNow() - _started).TotalSeconds);
            _progress.Report(new ProgressNotificationValue { Progress = seconds, Message = $"{_label} ({seconds.ToString(CultureInfo.InvariantCulture)} s)" });
        }
    }
}
