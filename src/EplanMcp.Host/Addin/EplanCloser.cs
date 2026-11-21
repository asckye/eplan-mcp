namespace EplanMcp.Host.Addin;

/// <summary>
/// How asking EPLAN to close went: whether it exited, the windows on the <see cref="HarmlessWindows"/> list that were
/// closed on the way, and, when it did not exit, the other windows that were open at the last look, which were left
/// alone.
/// </summary>
internal sealed record EplanClose(bool Closed, IReadOnlyList<ClosedWindow> ClosedWindows, IReadOnlyList<EplanWindow> InTheWay)
{
    /// <summary>EPLAN is gone without anything being closed: it exited by itself or never started.</summary>
    public static EplanClose Gone { get; } = new(true, [], []);

    /// <summary>EPLAN was not asked to close and is still running.</summary>
    public static EplanClose LeftOpen { get; } = new(false, [], []);
}

/// <summary>A window the closer closed, and the <see cref="HarmlessWindow.Name"/> of the list entry it matched.</summary>
internal sealed record ClosedWindow(EplanWindow Window, string Name);

/// <summary>
/// Closes an EPLAN the way its close buttons do, for install-addin, eplan_session_close and eplan_session_restart;
/// never forces it (ADR-0063, #664).
/// </summary>
internal static class EplanCloser
{
    /// <summary>
    /// Asks EPLAN to close, refreshing the process first every time: .NET's <c>Process</c> keeps the main window handle
    /// it first found, early in EPLAN's start the splash screen, and a request sent there does nothing (api-notes A23).
    /// Before each request it closes the windows of EPLAN on the <see cref="HarmlessWindows"/> list: while the modal
    /// system messages window is open, the main window is disabled and a close request is not even sent (A172). Asks
    /// again every <see cref="AddinTimings.Resend"/> in case a request did not take. Not closed when EPLAN is still
    /// running after <see cref="AddinTimings.Close"/>, for example behind a dialog that is not on the list.
    /// </summary>
    public static async Task<EplanClose> CloseAsync(IStartedEplan eplan, TimeProvider time, AddinTimings timings, CancellationToken cancellationToken, bool closeHarmless = true)
    {
        long start = time.GetTimestamp();
        TimeSpan? asked = null;
        var closed = new List<ClosedWindow>();
        IReadOnlyList<EplanWindow> inTheWay = [];
        while (!eplan.HasExited)
        {
            TimeSpan elapsed = time.GetElapsedTime(start);
            if (elapsed >= timings.Close)
            {
                return new(false, closed, inTheWay);
            }

            if (asked is null || elapsed - asked >= timings.Resend)
            {
                inTheWay = Sweep(eplan, closed, closeHarmless);
                eplan.Refresh();
                eplan.CloseMainWindow();
                asked = elapsed;
            }

            await Task.Delay(timings.Poll, time, cancellationToken).ConfigureAwait(false);
        }

        return new(true, closed, []);
    }

    /// <summary>
    /// Closes the windows on the list, when <paramref name="close"/> (ADR-0063 decision 8), and adds them to
    /// <paramref name="closed"/> once each; returns the other windows that can keep EPLAN open: dialogs and windows with
    /// an owner, other than EPLAN's main frame, and the listed ones left open.
    /// </summary>
    internal static List<EplanWindow> Sweep(IStartedEplan eplan, List<ClosedWindow> closed, bool close = true)
    {
        IReadOnlyList<EplanWindow> windows = eplan.Windows();
        var others = new List<EplanWindow>();
        foreach (EplanWindow window in windows)
        {
            if (!close && HarmlessWindows.Find(window, windows) is not null)
            {
                others.Add(window);
            }
            else if (HarmlessWindows.Find(window, windows) is { } entry)
            {
                eplan.CloseWindow(window);
                if (!closed.Any(c => c.Window.Handle == window.Handle))
                {
                    closed.Add(new(window, entry.Name));
                }
            }
            else if (!HarmlessWindows.IsMainFrame(window) && (window.Owner != 0 || window.ClassName == "#32770"))
            {
                others.Add(window);
            }
        }

        return others;
    }
}
