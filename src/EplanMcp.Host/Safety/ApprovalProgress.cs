using System.Globalization;
using ModelContextProtocol;

namespace EplanMcp.Host.Safety;

/// <summary>
/// Tells the client, while a question waits for the engineer, where it waits and for how long (ADR-0011 section 2):
/// every <see cref="Interval"/> until the deadline, as seconds of the deadline. The reports reach the client only when
/// it sent a progress token: the SDK drops them otherwise. No report is made after <see cref="Dispose"/> returns, so
/// none follows the tool's result.
/// </summary>
internal sealed class ApprovalProgress : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly Lock _lock = new();
    private readonly IProgress<ProgressNotificationValue> _progress;
    private readonly bool _release;
    private readonly DateTimeOffset _asked;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _clock;
    private readonly ITimer _timer;
    private string _where = string.Empty;
    private bool _stopped;

    private ApprovalProgress(IProgress<ProgressNotificationValue> progress, ApprovalQuestion question, TimeSpan timeout, TimeProvider clock)
    {
        _progress = progress;
        _release = question.Release is not null;
        _asked = question.Deadline - timeout;
        _timeout = timeout;
        _clock = clock;
        _timer = clock.CreateTimer(_ => Report(), null, Interval, Interval);
    }

    /// <summary>Starts reporting on <paramref name="question"/>; null without a progress to report to.</summary>
    public static ApprovalProgress? Start(IProgress<ProgressNotificationValue>? progress, ApprovalQuestion question, TimeSpan timeout, TimeProvider clock) =>
        progress is null ? null : new ApprovalProgress(progress, question, timeout, clock);

    /// <summary>The channel now asking, as its <see cref="IApprovalChannel.Where"/>.</summary>
    public void AskingIn(string where)
    {
        lock (_lock)
        {
            _where = where;
        }
    }

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
            TimeSpan waited = _clock.GetUtcNow() - _asked;
            if (_stopped || waited > _timeout)
            {
                return;
            }

            long seconds = (long)Math.Round(waited.TotalSeconds);
            string of = $"{seconds.ToString(CultureInfo.InvariantCulture)} s of {HostConfig.Seconds(_timeout)} s";
            _progress.Report(new ProgressNotificationValue
            {
                Progress = seconds,
                Total = (float)_timeout.TotalSeconds,
                Message = _release
                    ? $"Waiting for the engineer to confirm {_where} that the project was checked ({of})"
                    : $"Waiting for the engineer's approval {_where} ({of})",
            });
        }
    }
}
