using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Addin;

/// <summary>What one look at an EPLAN's windows did: the listed windows it closed, and the other windows in the way.</summary>
internal sealed record WindowWatchResult(IReadOnlyList<ClosedWindow> Closed, IReadOnlyList<EplanWindow> InTheWay)
{
    public static WindowWatchResult Nothing { get; } = new([], []);

    /// <summary>A sentence for a result or an error: what was closed and what is in the way; empty when neither.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Closed.Count > 0)
        {
            parts.Add($"EplanMcp closed EPLAN's {string.Join(", ", Closed.Select(c => c.Name).Distinct())} window; what it showed is in EPLAN's system log, EplLog.eldb.");
        }

        if (InTheWay.Count > 0)
        {
            parts.Add($"EPLAN shows {string.Join(", ", InTheWay)}; ask the engineer to answer it.");
        }

        return string.Join(" ", parts);
    }
}

/// <summary>
/// The EPLAN window watch (ADR-0063): looks at the windows of the connected EPLAN when a call found it busy, closes those
/// on the <see cref="HarmlessWindows"/> list unless <c>eplanWindowWatch</c> is <c>report</c>, records each closed one
/// in the audit, and names any other window in the way. It never answers a question for the engineer.
/// </summary>
internal sealed partial class WindowWatch(IEplanProcesses processes, AuditLog audit, HostConfig config, ILogger<WindowWatch> logger)
{
    public bool Closes => config.EplanWindowWatch == EplanWindowWatch.Close;

    /// <summary>One look at the windows of <paramref name="processId"/>; nothing when that process is gone.</summary>
    public async Task<WindowWatchResult> SweepAsync(int processId, string trigger, CancellationToken cancellationToken)
    {
        using IStartedEplan? eplan = processes.Attach(processId);
        if (eplan is null || eplan.HasExited)
        {
            return WindowWatchResult.Nothing;
        }

        var closed = new List<ClosedWindow>();
        List<EplanWindow> inTheWay = EplanCloser.Sweep(eplan, closed, Closes);
        await RecordAsync(closed, processId, trigger, cancellationToken).ConfigureAwait(false);
        return new WindowWatchResult(closed, inTheWay);
    }

    /// <summary>One audit record per closed window (decision 3): its list entry, class and title, the process and what led to it.</summary>
    public async Task RecordAsync(IReadOnlyList<ClosedWindow> closed, int processId, string trigger, CancellationToken cancellationToken)
    {
        foreach (ClosedWindow window in closed)
        {
            LogClosed(window.Name, processId, trigger);
            var record = new JsonObject
            {
                ["kind"] = "eplan_window_closed",
                ["window"] = window.Name,
                ["class"] = window.Window.ClassName,
                ["title"] = Plans.PlanDiffPreview.Escape(window.Window.Title),
                ["eplan"] = new JsonObject { ["processId"] = processId },
                ["trigger"] = trigger,
            };
            try
            {
                await audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
            }
            catch (AuditUnavailableException ex)
            {
                LogAuditUnavailable(ex); // the window is closed already; the log line above still says so
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Closed EPLAN's {Window} window in process {ProcessId} ({Trigger})")]
    private partial void LogClosed(string window, int processId, string trigger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The audit log cannot be written")]
    private partial void LogAuditUnavailable(Exception ex);
}
