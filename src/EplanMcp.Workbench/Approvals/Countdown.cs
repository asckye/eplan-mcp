using System.Globalization;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// The countdown of a waiting question (workbench-ui APR-10): the time left as the EPLAN dialog writes it, the share
/// left for the progress bar, the warning in the last 20 seconds, and the two moments a screen reader is told, 30 and
/// 10 seconds before the end, never every second. The deadline is the Host's and cannot be extended here.
/// </summary>
internal static class Countdown
{
    /// <summary>From this much time left, text and bar take the warning colour and the warning icon shows.</summary>
    public static readonly TimeSpan WarningFrom = TimeSpan.FromSeconds(20);

    /// <summary>The seconds left at which the time is announced.</summary>
    public static IReadOnlyList<int> Announced { get; } = [30, 10];

    public static TimeSpan Left(Question question, DateTimeOffset now) => question.Deadline > now ? question.Deadline - now : TimeSpan.Zero;

    /// <summary><c>m:ss</c>, whole seconds rounded up, as the EPLAN dialog (Bridge's ApprovalCountdown).</summary>
    public static string Clock(TimeSpan left)
    {
        int seconds = (int)Math.Ceiling(Math.Max(0, left.TotalSeconds));
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", seconds / 60, seconds % 60);
    }

    public static bool IsWarning(TimeSpan left) => left <= WarningFrom;

    /// <summary>The share of the question's time still left: 1 when it arrived, 0 at its deadline.</summary>
    public static double Fraction(Question question, DateTimeOffset now)
    {
        TimeSpan total = question.Deadline - question.Arrived;
        return total <= TimeSpan.Zero ? 0 : Math.Clamp(Left(question, now) / total, 0, 1);
    }

    /// <summary>The announced moment passed between two readings of the time left, the later of two; null when none was.</summary>
    public static int? Crossed(TimeSpan before, TimeSpan after)
    {
        int? crossed = null;
        foreach (int seconds in Announced)
        {
            if (before.TotalSeconds > seconds && after.TotalSeconds <= seconds)
            {
                crossed = seconds;
            }
        }

        return crossed;
    }
}
