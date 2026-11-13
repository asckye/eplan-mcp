namespace EplanMcp.Bridge.Core.Operations;

/// <summary>How the approval dialog's modal loop ended, for the log.</summary>
public enum ApprovalModalEnd
{
    /// <summary>The window closed and ShowDialog returned on its own.</summary>
    Closed,

    /// <summary>The window was destroyed without closing; the guard ended the modal loop.</summary>
    Destroyed,

    /// <summary>The window closed (or was asked to) but the modal loop ran on; the guard ended it.</summary>
    Stuck,
}

/// <summary>
/// The approval dialog's guard against a modal loop that outlives its window (2026-11-12: after an approval, EPLAN's
/// main window stayed disabled with no dialog left, and every call was EPLAN_BUSY until EPLAN restarted). Ending the
/// loop from outside skips WPF's own re-enabling of the windows ShowDialog disabled, so whoever ends it re-enables the
/// windows that were enabled before (<see cref="ToReenable"/>). No EPLAN API; tested without a window.
/// </summary>
public static class ApprovalModalGuard
{
    /// <summary>How long a closed or close-requested window may keep the modal loop running.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Why the modal loop must be ended now, or null to let it run: the window is gone, or it closed or was asked to
    /// close at <paramref name="closeAt"/> and the loop still runs after <see cref="Grace"/>.
    /// </summary>
    public static ApprovalModalEnd? Exit(bool windowAlive, TimeSpan? closeAt, TimeSpan open) =>
        !windowAlive ? ApprovalModalEnd.Destroyed
        : closeAt is { } at && open - at >= Grace ? ApprovalModalEnd.Stuck
        : null;

    /// <summary>The windows enabled before the dialog that still exist and are disabled after it.</summary>
    public static IReadOnlyList<IntPtr> ToReenable(IEnumerable<IntPtr> enabledBefore, Func<IntPtr, bool> exists, Func<IntPtr, bool> enabled) =>
        [.. enabledBefore.Where(w => exists(w) && !enabled(w))];
}
