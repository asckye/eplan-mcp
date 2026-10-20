using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What the engineer did in the approval dialog.</summary>
public enum ApprovalChoice
{
    Approve,
    Reject,

    /// <summary>Closed the window (Esc, the close box) without choosing.</summary>
    Close,
}

/// <summary>
/// The approval dialog's timing rules, apart from the window so that they are tested without EPLAN. Borrowed from the
/// TIA project's approval card: clicks in the first <see cref="ClickGuard"/> after the dialog appears are ignored, so a
/// click meant for something else cannot approve; the time left is shown; at the deadline the dialog closes itself as
/// expired, and nothing done after it counts as approval. <c>open</c> is the time since the dialog was opened, which
/// the deadline counts from, so that it expires even if it never appears; <c>shown</c> is the time since it appeared on
/// screen, null before, which the click guard counts from.
/// </summary>
public sealed class ApprovalCountdown(TimeSpan timeout)
{
    public static readonly TimeSpan ClickGuard = TimeSpan.FromMilliseconds(500);

    /// <summary>Whether the buttons take clicks.</summary>
    public bool AcceptsClicks(TimeSpan open, TimeSpan? shown) => shown >= ClickGuard && !HasExpired(open);

    public bool HasExpired(TimeSpan open) => open >= timeout;

    /// <summary>The time left as m:ss, rounded up, never negative.</summary>
    public string Remaining(TimeSpan open)
    {
        int seconds = (int)Math.Ceiling(Math.Max(0, (timeout - open).TotalSeconds));
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", seconds / 60, seconds % 60);
    }

    /// <summary>The decision for <paramref name="choice"/>, one of <see cref="ApprovalDecisions"/>; null for a click to ignore.</summary>
    public string? Decide(ApprovalChoice choice, TimeSpan open, TimeSpan? shown) =>
        HasExpired(open) ? ApprovalDecisions.Expired
        : choice == ApprovalChoice.Close ? ApprovalDecisions.Cancelled
        : !AcceptsClicks(open, shown) ? null
        : choice == ApprovalChoice.Approve ? ApprovalDecisions.Granted
        : ApprovalDecisions.Denied;
}
