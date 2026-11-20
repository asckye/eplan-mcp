using System.ComponentModel;
using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Addin;

/// <summary>
/// One start of EPLAN for install-addin: <see cref="Result"/> with the process ID (and <see cref="AddinNote.CloseEplan"/>
/// when EPLAN did not close, <see cref="AddinNote.SystemMessagesClosed"/> when its system messages window was closed),
/// what went wrong, the handshake of its Bridge, and how closing EPLAN went.
/// </summary>
internal sealed record Launch(AddinResult Result, AddinIssue? Problem, HandshakeResult? Handshake, EplanClose Close)
{
    public bool Closed => Close.Closed;
}

/// <summary>
/// Starts EPLAN with an <c>EplApiModuleAction</c> command, waits for the Bridge pipe of the process it started, asks the
/// Bridge which add-in EPLAN loaded, and closes that EPLAN as its close button would, asking again until it does or
/// the wait ends. It never forces EPLAN to close.
/// </summary>
internal sealed class EplanLauncher(IEplanProcesses processes, IBridgeProbe bridge, TimeProvider time, AddinTimings timings)
{
    public const int HandshakeTries = 3;

    /// <summary>
    /// Up: handshakes, waits <paramref name="settle"/>, calls <paramref name="loaded"/> with the handshake (null when the
    /// Bridge never answered) and closes EPLAN. Still starting at the end of the wait: leaves it open (<see cref="AddinProblem.NotLoaded"/>).
    /// Exited first: <see cref="AddinProblem.EplanExited"/>.
    /// </summary>
    public async Task<Launch> RunAsync(AddinResult result, string arguments, TimeSpan settle, Action<HandshakeResult?> loaded)
    {
        IStartedEplan started;
        try
        {
            started = processes.Start(result.Eplan, arguments);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new(result, new(AddinProblem.StartFailed, ex.Message), null, EplanClose.Gone);
        }

        using (started)
        {
            string pid = started.Id.ToString(CultureInfo.InvariantCulture);
            result = result with { ProcessId = started.Id };
            switch (await WaitForPipeAsync(started).ConfigureAwait(false))
            {
                case null:
                    return new(result, new(AddinProblem.EplanExited, pid), null, EplanClose.Gone);
                case false:
                    return new(result, new(AddinProblem.NotLoaded, pid), null, EplanClose.LeftOpen);
            }

            HandshakeResult? handshake = await HandshakeAsync(started.Id).ConfigureAwait(false);
            await Task.Delay(settle, time).ConfigureAwait(false);
            loaded(handshake);
            EplanClose close = await CloseAsync(started).ConfigureAwait(false);
            IEnumerable<AddinNote> notes = result.Notes;
            if (close.ClosedWindows.Count > 0 && !result.Notes.Contains(AddinNote.SystemMessagesClosed))
            {
                notes = notes.Append(AddinNote.SystemMessagesClosed);
            }

            if (!close.Closed)
            {
                notes = notes.Append(AddinNote.CloseEplan);
            }

            return new(result with { Notes = [.. notes] }, null, handshake, close);
        }
    }

    /// <summary>The handshake, tried <see cref="HandshakeTries"/> times in case the Bridge was still busy starting; null when it never answered.</summary>
    private async Task<HandshakeResult?> HandshakeAsync(int processId)
    {
        for (int i = 1; ; i++)
        {
            if (await bridge.HandshakeAsync(processId, CancellationToken.None).ConfigureAwait(false) is { } handshake)
            {
                return handshake;
            }

            if (i == HandshakeTries)
            {
                return null;
            }

            await Task.Delay(timings.Poll, time).ConfigureAwait(false);
        }
    }

    /// <summary>True once the started EPLAN's Bridge pipe is up, false when it is still missing after the wait, null when EPLAN exited.</summary>
    private async Task<bool?> WaitForPipeAsync(IStartedEplan started)
    {
        long start = time.GetTimestamp();
        while (true)
        {
            if (bridge.IsUp(started.Id))
            {
                return true;
            }

            if (started.HasExited)
            {
                return null;
            }

            if (time.GetElapsedTime(start) >= timings.Load)
            {
                return false;
            }

            await Task.Delay(timings.Poll, time).ConfigureAwait(false);
        }
    }

    private Task<EplanClose> CloseAsync(IStartedEplan started) => EplanCloser.CloseAsync(started, time, timings, CancellationToken.None);
}
