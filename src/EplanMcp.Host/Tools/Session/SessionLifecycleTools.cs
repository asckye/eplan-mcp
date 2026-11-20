using System.ComponentModel;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Addin;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Starting, closing and restarting EPLAN from the Host (ADR-0058, docs/tools/eplan_session_start.md,
/// eplan_session_close.md, eplan_session_restart.md). Parameter names are snake_case because the SDK publishes them as written.
/// </summary>
[McpServerToolType]
internal sealed class SessionLifecycleTools
{
    public const string StartTool = "eplan_session_start";
    public const string CloseTool = "eplan_session_close";
    public const string RestartTool = "eplan_session_restart";

    [McpServerTool(Name = StartTool, Title = "Start EPLAN with the EplanMcp add-in", ReadOnly = false, Destructive = false,
        Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SessionStartResult))]
    [Description(
        "Starts EPLAN on this computer and waits up to 3 minutes for its EplanMcp add-in to connect; use it when " +
        "eplan_session_status reports no EPLAN. Refuses while any EPLAN runs. Opens no project: then call " +
        "eplan_project_open. Takes an EPLAN licence and shows EPLAN's window. Example: {\"variant\": \"Electric P8\"}.")]
    public static async Task<CallToolResult> StartAsync(
        EplanLifecycle lifecycle,
        IProgress<ProgressNotificationValue> progress,
        [Description("Product variant, e.g. Electric P8 or Pro Panel; default Electric P8")] string? variant = null,
        CancellationToken cancellationToken = default)
    {
        (SessionStartResult? result, BridgeError? error) = await StartCoreAsync(lifecycle, progress, variant, cancellationToken).ConfigureAwait(false);
        return result is null ? ToolResults.Error(error!) : ToolResults.Success(Started(result), result);
    }

    [McpServerTool(Name = CloseTool, Title = "Close EPLAN when only scratch projects are open", ReadOnly = false, Destructive = true,
        Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SessionCloseResult))]
    [Description(
        "Closes the connected EPLAN as its close button would, waiting up to 1 minute; never forces it. Refuses while a " +
        "project outside the scratch roots is open, a write or plan task runs, or a write is suspended. dry_run (default " +
        "true) only reports these checks. Example: {\"process_id\": 4711, \"dry_run\": false}.")]
    public static async Task<CallToolResult> CloseAsync(
        EplanLifecycle lifecycle, IBridgeClient bridge, WritePipeline writes, TaskRegistry tasks, SuspensionStore suspensions, HostConfig config,
        [Description("EPLAN's process ID from eplan_session_status; it must be the connected instance")] int process_id,
        [Description("True (default) only reports the checks; false closes EPLAN")] bool dry_run = true,
        CancellationToken cancellationToken = default)
    {
        (SessionCloseResult? result, BridgeError? error) = await CloseCoreAsync(lifecycle, bridge, writes, tasks, suspensions, config, process_id, dry_run, cancellationToken)
            .ConfigureAwait(false);
        return result is null ? ToolResults.Error(error!) : ToolResults.Success(Closed(result), result);
    }

    [McpServerTool(Name = RestartTool, Title = "Restart EPLAN when only scratch projects are open", ReadOnly = false, Destructive = true,
        Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SessionRestartResult))]
    [Description(
        "Closes the connected EPLAN like eplan_session_close (same checks, never forced), then starts the same variant " +
        "again like eplan_session_start and waits for the add-in, up to 4 minutes in all. Opens no project. Use it after " +
        "the add-in was replaced. dry_run (default true) only reports the close checks. Example: {\"process_id\": 4711, \"dry_run\": false}.")]
    public static async Task<CallToolResult> RestartAsync(
        EplanLifecycle lifecycle, IBridgeClient bridge, WritePipeline writes, TaskRegistry tasks, SuspensionStore suspensions, HostConfig config,
        IProgress<ProgressNotificationValue> progress,
        [Description("EPLAN's process ID from eplan_session_status; it must be the connected instance")] int process_id,
        [Description("True (default) only reports the close checks; false restarts EPLAN")] bool dry_run = true,
        CancellationToken cancellationToken = default)
    {
        // The variant and add-in version the closed EPLAN reported; restarting never switches products.
        HandshakeResult? before = (await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Handshake;
        (SessionCloseResult? closed, BridgeError? closeError) = await CloseCoreAsync(lifecycle, bridge, writes, tasks, suspensions, config, process_id, dry_run, cancellationToken)
            .ConfigureAwait(false);
        if (closed is null)
        {
            return ToolResults.Error(closeError!);
        }

        var result = new SessionRestartResult { Close = closed, BridgeVersionBefore = before?.BridgeVersion };
        if (dry_run)
        {
            return ToolResults.Success(Closed(closed), result);
        }

        (SessionStartResult? started, BridgeError? startError) = await StartCoreAsync(lifecycle, progress, before?.Eplan.Variant, cancellationToken).ConfigureAwait(false);
        if (started is null)
        {
            startError!.Message = "EPLAN is closed and was not started again: " + startError.Message;
            return ToolResults.Error(startError);
        }

        result.Start = started;
        return ToolResults.Success(Started(started), result);
    }

    private static string Started(SessionStartResult result) => string.Create(CultureInfo.InvariantCulture,
        $"Started EPLAN {result.Version} ({result.Variant}), process {result.ProcessId}, connected after {result.Seconds:0} s.");

    private static string Closed(SessionCloseResult result) => result.Execution == ExecutionState.Committed
        ? string.Create(CultureInfo.InvariantCulture, $"Closed EPLAN process {result.ProcessId}.")
            + (result.ClosedWindows.Count > 0
                ? $" Its {string.Join(", ", result.ClosedWindows)} window was closed first; what it showed is in EPLAN's system log, EplLog.eldb."
                : string.Empty)
        : "Dry run: EPLAN can be closed; call again with dry_run: false.";

    private static async Task<(SessionStartResult? Result, BridgeError? Error)> StartCoreAsync(
        EplanLifecycle lifecycle, IProgress<ProgressNotificationValue> progress, string? variant, CancellationToken cancellationToken)
    {
        int step = 0;
        EplanStart started;
        try
        {
            started = await lifecycle.StartAsync(variant, message => progress.Report(new ProgressNotificationValue { Progress = ++step, Message = message }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EplanLifecycleException ex)
        {
            return (null, Error(ex));
        }

        return (new SessionStartResult
        {
            ProcessId = started.ProcessId,
            Variant = started.Variant,
            Version = started.Version,
            BridgeVersion = started.Handshake.BridgeVersion,
            Seconds = started.Seconds,
            Hint = "No project is open; call eplan_project_open, or eplan_project_list for what EPLAN opened by itself.",
        }, null);
    }

    private static async Task<(SessionCloseResult? Result, BridgeError? Error)> CloseCoreAsync(
        EplanLifecycle lifecycle, IBridgeClient bridge, WritePipeline writes, TaskRegistry tasks, SuspensionStore suspensions, HostConfig config,
        int processId, bool dryRun, CancellationToken cancellationToken)
    {
        Checked check;
        try
        {
            check = await CheckAsync(lifecycle, bridge, writes, tasks, suspensions, config, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (EplanLifecycleException ex)
        {
            return (null, Error(ex));
        }
        catch (BridgeCallException ex)
        {
            return (null, ex.Error);
        }

        if (check.Refusal is { } refusal)
        {
            return (null, refusal);
        }

        if (dryRun)
        {
            return (check.Result, null);
        }

        EplanClose closed;
        try
        {
            closed = await lifecycle.CloseAsync(processId, cancellationToken).ConfigureAwait(false);
        }
        catch (EplanLifecycleException ex)
        {
            return (null, Error(ex));
        }

        if (!closed.Closed)
        {
            string windows = string.Join(", ", closed.InTheWay);
            var metadata = new Dictionary<string, string>();
            if (closed.InTheWay.Count > 0)
            {
                metadata["windows"] = windows;
            }

            return (null, new BridgeError
            {
                Reason = ErrorReason.Timeout,
                Retryable = false,
                Message = closed.InTheWay.Count > 0
                    ? $"EPLAN was asked to close but is still running after 1 minute behind {windows}. It was not forced and that window was left alone; ask the engineer to answer it."
                    : "EPLAN was asked to close but is still running after 1 minute; it may show a dialog. It was not forced; ask the engineer to look at it.",
                Metadata = metadata,
            });
        }

        check.Result.ClosedWindows = [.. closed.ClosedWindows.Select(c => c.Name).Distinct()];
        check.Result.Execution = ExecutionState.Committed;
        return (check.Result, null);
    }

    private static BridgeError Error(EplanLifecycleException ex) => ex.Reason == ErrorReason.InvalidArgument
        ? ToolResults.InvalidArgumentError([ex.Message])
        : new BridgeError { Reason = ex.Reason, Message = ex.Message, Retryable = false, Metadata = new Dictionary<string, string>(ex.Metadata) };

    /// <summary>
    /// The checks before closing (ADR-0058): this Host must be connected to that EPLAN, no write, approval, plan task or
    /// suspended write may be waiting, and every open project must be under a scratch root. A refusal is returned, not
    /// thrown, so that the result still lists every check.
    /// </summary>
    private static async Task<Checked> CheckAsync(EplanLifecycle lifecycle, IBridgeClient bridge, WritePipeline writes, TaskRegistry tasks,
        SuspensionStore suspensions, HostConfig config, int processId, CancellationToken cancellationToken)
    {
        if (lifecycle.Pinned)
        {
            throw new EplanLifecycleException(ErrorReason.CapabilityMissing,
                "This EplanMcp serves one EPLAN process over HTTP and cannot close it; ask the engineer to close EPLAN in the virtual machine.");
        }

        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Handshake?.Eplan.ProcessId != processId)
        {
            throw new EplanLifecycleException(ErrorReason.SessionNotSelected,
                "Only the EPLAN this EplanMcp is connected to can be closed; select it with eplan_session_select first.",
                new Dictionary<string, string> { ["processes"] = string.Join(", ", status.Instances.Select(i => i.ProcessId.ToString(CultureInfo.InvariantCulture))) });
        }

        ProjectListData open = await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new ProjectListArgs(), cancellationToken).ConfigureAwait(false);
        var result = new SessionCloseResult
        {
            ProcessId = processId,
            WritesInFlight = writes.InFlight,
            PlanTasks = tasks.Unfinished,
            // Only a suspension of a project open in this EPLAN concerns closing it (live 2026-11-15: a suspension of
            // a project closed weeks ago blocked the close).
            SuspendedWrites = suspensions.All().Count(s => s.Project is { } suspended
                && open.Projects.Any(p => string.Equals(p.Path, suspended, StringComparison.OrdinalIgnoreCase))),
            Projects = [.. open.Projects.Select(p => new SessionCloseResult.ProjectEntry { Name = p.Name, InScratch = ScratchPaths.IsInside(p.Path, config.ScratchRoots) })],
            Execution = ExecutionState.NotStarted,
        };
        BridgeError? refusal = null;
        if (result.WritesInFlight > 0 || result.PlanTasks > 0)
        {
            refusal = new BridgeError
            {
                Reason = ErrorReason.EplanBusy,
                Retryable = true,
                Message = "A write, an approval or a plan task is still running; wait for it to finish, then call again.",
            };
        }
        else if (result.SuspendedWrites > 0)
        {
            refusal = new BridgeError
            {
                Reason = ErrorReason.WritesSuspended,
                Retryable = false,
                Message = "A write with an unknown outcome is suspended; ask the engineer to check it in EPLAN and confirm it first.",
            };
        }
        else if (result.Projects.Any(p => !p.InScratch))
        {
            refusal = new BridgeError
            {
                Reason = ErrorReason.PolicyDenied,
                Retryable = false,
                Message = "Projects outside the scratch roots are open: " + string.Join(", ", result.Projects.Where(p => !p.InScratch).Select(p => p.Name)) +
                    ". Ask the engineer to close them in EPLAN; nothing was closed.",
            };
        }

        return new Checked(result, refusal);
    }

    private sealed record Checked(SessionCloseResult Result, BridgeError? Refusal);
}

/// <summary>Output of <c>eplan_session_close</c>.</summary>
internal sealed class SessionCloseResult
{
    [Description("The EPLAN process checked or closed.")]
    public int ProcessId { get; set; }

    [Description("Write calls running now, approvals they wait on included; closing waits for 0.")]
    public int WritesInFlight { get; set; }

    [Description("Plan tasks queued or running; closing waits for 0.")]
    public int PlanTasks { get; set; }

    [Description("Writes suspended with an unknown outcome; the engineer confirms them first.")]
    public int SuspendedWrites { get; set; }

    [Description("The open projects, by name, and whether each is under a scratch root.")]
    public List<ProjectEntry> Projects { get; set; } = [];

    [Description("not_started (dry run or refused) or committed (EPLAN exited).")]
    public string Execution { get; set; } = string.Empty;

    [Description("EPLAN windows closed so that EPLAN could close, such as 'system messages'; their messages stay in EPLAN's system log.")]
    public List<string> ClosedWindows { get; set; } = [];

    internal sealed class ProjectEntry
    {
        [Description("Project name as EPLAN shows it.")]
        public string Name { get; set; } = string.Empty;

        [Description("True when the project is under a scratch root.")]
        public bool InScratch { get; set; }
    }
}

/// <summary>Output of <c>eplan_session_restart</c>.</summary>
internal sealed class SessionRestartResult
{
    [Description("The close checks, and whether EPLAN was closed.")]
    public SessionCloseResult Close { get; set; } = new();

    [Description("The EplanMcp add-in version before the restart, to compare with start.bridgeVersion after replacing the add-in.")]
    public string? BridgeVersionBefore { get; set; }

    [Description("The started EPLAN; null on a dry run.")]
    public SessionStartResult? Start { get; set; }
}

/// <summary>Output of <c>eplan_session_start</c>.</summary>
internal sealed class SessionStartResult
{
    [Description("The Windows process ID of the EPLAN started, as eplan_session_select takes it.")]
    public int ProcessId { get; set; }

    [Description("The product variant started.")]
    public string Variant { get; set; } = string.Empty;

    [Description("EPLAN's file version.")]
    public string Version { get; set; } = string.Empty;

    [Description("The EplanMcp add-in version that answered.")]
    public string BridgeVersion { get; set; } = string.Empty;

    [Description("Seconds from the start to the add-in's handshake.")]
    public double Seconds { get; set; }

    [Description("What to do next.")]
    public string? Hint { get; set; }
}
