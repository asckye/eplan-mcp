namespace EplanMcp.Host.Addin;

/// <summary>Closes an EPLAN the way its close button does, for install-addin and eplan_session_close; never forces it.</summary>
internal static class EplanCloser
{
    /// <summary>
    /// Asks EPLAN to close, refreshing the process first every time: .NET's <c>Process</c> keeps the main window handle
    /// it first found, early in EPLAN's start the splash screen, and a request sent there does nothing (api-notes A23).
    /// Asks again every <see cref="AddinTimings.Resend"/> in case a request did not take. False when EPLAN is still
    /// running after <see cref="AddinTimings.Close"/>, for example behind a system messages window.
    /// </summary>
    public static async Task<bool> CloseAsync(IStartedEplan eplan, TimeProvider time, AddinTimings timings, CancellationToken cancellationToken)
    {
        long start = time.GetTimestamp();
        TimeSpan? asked = null;
        while (!eplan.HasExited)
        {
            TimeSpan elapsed = time.GetElapsedTime(start);
            if (elapsed >= timings.Close)
            {
                return false;
            }

            if (asked is null || elapsed - asked >= timings.Resend)
            {
                eplan.Refresh();
                eplan.CloseMainWindow();
                asked = elapsed;
            }

            await Task.Delay(timings.Poll, time, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
