using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace EplanMcp.Host.Safety;

/// <summary>
/// One write call as a tool states it: its tier, the Bridge operations that plan and execute it, the arguments of both,
/// whether it is a dry run, how long it may run (M3 design 4.3, 4.4), and what the client is told while it runs: a short
/// English phrase such as "Creating pages" (M4 design 4.3). A Destructive write's execute arguments carry the
/// <see cref="BackupTarget"/> the pipeline made for it (ADR-0013); other writes get null.
/// </summary>
internal sealed record WriteRequest<TArgs, TExecuteArgs>(
    string Tool,
    ToolTier Tier,
    string PlanOperation,
    string ExecuteOperation,
    TArgs Args,
    Func<WritePlan, BackupTarget?, TExecuteArgs> ExecuteArgs,
    bool DryRun,
    TimeSpan Limit,
    string Activity = "Waiting for EPLAN")
{
    /// <summary>A write whose execute arguments need no backup target; named like the record's own parameters.</summary>
    public WriteRequest(string Tool, ToolTier Tier, string PlanOperation, string ExecuteOperation, TArgs Args, Func<WritePlan, TExecuteArgs> ExecuteArgs, bool DryRun, TimeSpan Limit, string Activity = "Waiting for EPLAN")
        : this(Tool, Tier, PlanOperation, ExecuteOperation, Args, (plan, _) => ExecuteArgs(plan), DryRun, Limit, Activity)
    {
    }
}

/// <summary>
/// What a write call did: its plan, the execution state, the data of a commit that answered in time, and for a committed
/// Destructive write the path of its backup copy.
/// </summary>
internal sealed record WriteOutcome<TData>(WritePlan Plan, string Execution, TData? Data, bool Recovered, string? Backup = null);

/// <summary>
/// The steps every write tool goes through (M3 design 3, ADR-0005, ADR-0010): plan, dry run, policy, the confirmation
/// that lifts a suspension, approval, the "started" audit record, one execute that is never sent twice, the result
/// record, and a suspension when the outcome is unknown. A Destructive write, always approved first, also gets a new
/// backup folder before it starts; the Bridge copies the project there before it changes anything (ADR-0013). Every
/// error it throws carries the execution state.
/// </summary>
internal sealed partial class WritePipeline(
    IBridgeClient bridge,
    HostConfig config,
    SuspensionStore suspensions,
    AuditLog audit,
    DanglingWrites dangling,
    ApprovalService approvals,
    BackupFolders backups,
    TimeProvider clock,
    ILogger<WritePipeline> logger)
{
    /// <summary>Runs <paramref name="request"/> without reporting progress.</summary>
    public Task<WriteOutcome<TData>> RunAsync<TArgs, TExecuteArgs, TData>(WriteRequest<TArgs, TExecuteArgs> request, CancellationToken cancellationToken) =>
        RunAsync<TArgs, TExecuteArgs, TData>(request, progress: null, cancellationToken);

    /// <summary>
    /// Runs <paramref name="request"/>. <paramref name="progress"/> is the tool's <c>IProgress</c> parameter, which the
    /// SDK binds to the client's progress token: while the engineer is asked (ADR-0011) and while EPLAN executes the
    /// write (M4 design 4.3), the wait is reported there.
    /// </summary>
    public async Task<WriteOutcome<TData>> RunAsync<TArgs, TExecuteArgs, TData>(WriteRequest<TArgs, TExecuteArgs> request, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        CallProgress? call = CallProgress.For(progress);
        WritePlan plan;
        try
        {
            plan = await bridge.InvokeAsync<TArgs, WritePlan>(request.PlanOperation, request.Args, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            throw NotStarted(ex.Error);
        }

        // Only a valid, non-empty plan is ever put to the engineer.
        if (request.DryRun || plan.Changes.Count == 0)
        {
            return new WriteOutcome<TData>(plan, ExecutionState.NotStarted, default, Recovered: false);
        }

        HandshakeResult eplan = (await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Handshake
            ?? throw NotStarted(BridgeErrors.NotConnected());
        DateTimeOffset started = SuspensionStore.StartOf(eplan.Eplan.ProcessId) ?? throw NotStarted(BridgeErrors.ConnectionLost());
        try
        {
            // A write a crashed Host left without an outcome may suspend this project first (ADR-0010 section 2).
            await dangling.SettleOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AuditUnavailableException ex)
        {
            LogAuditUnavailable(ex);
            throw NotStarted(new BridgeError { Reason = ErrorReason.AuditUnavailable, Message = AuditUnavailableMessage });
        }

        // Nobody is asked anything about a write the policy forbids anyway.
        PolicyDecision decision = PolicyEngine.Decide(request.Tier, ScratchPaths.IsInside(plan.ProjectPath, config.ScratchRoots), config);
        if (decision.Verdict == PolicyVerdict.Deny)
        {
            throw NotStarted(new BridgeError { Reason = ErrorReason.PolicyDenied, Message = decision.Message! });
        }

        // The Host names the write, never a client: the approval and the execution are bound to this ID.
        string requestId = Guid.NewGuid().ToString("N");
        string argumentsHash = HashArguments(request.Args);
        if (suspensions.Blocking(plan.ProjectPath, eplan.Eplan.ProcessId, started) is { Count: > 0 } blocking)
        {
            await ReleaseAsync(request.Tool, request.Tier, requestId, argumentsHash, plan, eplan, started, blocking, call, cancellationToken).ConfigureAwait(false);
        }

        bool destructive = request.Tier == ToolTier.Destructive;
        if (decision.Verdict == PolicyVerdict.NeedsApproval)
        {
            ApprovalResult approval;
            try
            {
                string? backupFolder = destructive ? backups.ProjectFolder(plan) : null;
                approval = await approvals.AskAsync(requestId, request.Tool, request.Tier, argumentsHash, plan, eplan.Eplan, backupFolder, call?.Lane(), cancellationToken).ConfigureAwait(false);
            }
            catch (BridgeCallException ex)
            {
                throw NotStarted(ex.Error);
            }

            await RecordApprovalAsync(request.Tool, requestId, plan, eplan, approval).ConfigureAwait(false);
            if (approval.Decision != ApprovalDecision.Granted)
            {
                throw NotStarted(approvals.Refusal(approval.Decision));
            }
        }

        // Destructive writes were approved above in any case (PolicyEngine); the backup folder is made only after that.
        BackupTarget? backup = destructive ? NewBackup(plan, request.Limit) : null;
        JsonObject record = Record("execution_started", request.Tool, requestId, plan, eplan);
        record["eplan"]!["start"] = started.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        record["host"] = DanglingWrites.ThisHost();
        record["tier"] = request.Tier.ToString();
        record["policy"] = decision.Verdict.ToString();
        record["arguments"] = JsonSerializer.SerializeToNode(request.Args, HostJson.Options);
        record["changes"] = new JsonArray([.. ChangeLines(plan).Select(line => (JsonNode?)line)]);
        if (backup is not null)
        {
            record["backup"] = backup.Path;
        }

        try
        {
            await audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (AuditUnavailableException ex)
        {
            LogAuditUnavailable(ex);
            Settle(backup, ExecutionState.NotStarted, null);
            throw NotStarted(new BridgeError { Reason = ErrorReason.AuditUnavailable, Message = AuditUnavailableMessage });
        }

        // From here on the write may run: no cancellation may skip its result record. A cancel stops it in the Bridge
        // at its next safe point; what became of it is recorded all the same (M4 design 4.3).
        using BridgeProgress? waiting = BridgeProgress.Start(call, request.Activity, clock, cancellationToken);
        try
        {
            WriteResult<TData> result = await bridge.InvokeWriteAsync<TExecuteArgs, TData>(request.ExecuteOperation, request.ExecuteArgs(plan, backup), requestId, request.Limit, waiting, cancellationToken).ConfigureAwait(false);
            await RecordResultAsync(request.Tool, requestId, plan, eplan, ExecutionState.Committed, null, result.Recovered, backup).ConfigureAwait(false);
            return new WriteOutcome<TData>(plan, ExecutionState.Committed, result.Data, result.Recovered, backup?.Path);
        }
        catch (BridgeCallException ex)
        {
            string execution = ex.Error.Execution ?? ExecutionState.Unknown;
            await RecordResultAsync(request.Tool, requestId, plan, eplan, execution, ex.Error.Reason, recovered: false, backup).ConfigureAwait(false);
            if (execution == ExecutionState.Unknown)
            {
                suspensions.Suspend(plan.ProjectPath, eplan.Eplan.ProcessId, started, request.Tool, requestId, clock.GetUtcNow(), SuspensionReason.OutcomeUnknown, ChangeLines(plan));
                await AppendQuietlyAsync(Record("writes_suspended", request.Tool, requestId, plan, eplan)).ConfigureAwait(false);
            }

            Settle(backup, execution, ex.Error);
            throw;
        }
    }

    /// <summary>
    /// The backup target of a Destructive write: a new folder (<see cref="BackupFolders"/>) and half the execution limit,
    /// so that the change itself still ends within the limit (tool spec eplan_page_delete, section 6). BACKUP_FAILED
    /// when the folder cannot be made; nothing has run then.
    /// </summary>
    private BackupTarget NewBackup(WritePlan plan, TimeSpan limit)
    {
        try
        {
            return new BackupTarget { Path = backups.Create(plan), MaxSeconds = (int)(limit.TotalSeconds / 2) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogBackupFolderFailed(ex, plan.ProjectPath);
            throw new BridgeCallException(BackupFolders.Failure());
        }
    }

    /// <summary>
    /// What becomes of a Destructive write's backup folder: after a write that did not run it is renamed "-failed", or
    /// removed while still empty (<see cref="BackupFolders.Settle"/>); after one that rolled back or whose outcome is
    /// unknown the backup stays, and the error names it, so the engineer can restore from it.
    /// </summary>
    private void Settle(BackupTarget? backup, string execution, BridgeError? error)
    {
        if (backup is null)
        {
            return;
        }

        if (execution != ExecutionState.NotStarted)
        {
            if (error is not null)
            {
                error.Metadata ??= [];
                error.Metadata["backup"] = backup.Path;
            }

            return;
        }

        try
        {
            string? kept = BackupFolders.Settle(backup.Path, failed: error?.Reason == ErrorReason.BackupFailed);
            LogBackupFolderSettled(backup.Path, kept ?? "removed, empty");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogBackupFolderFailed(ex, backup.Path);
        }
    }

    internal const string AuditUnavailableMessage =
        "The audit log cannot be written, so writes are refused and nothing was changed. Ask the engineer to run 'eplanmcp doctor'.";

    /// <summary>The SHA-256 of the arguments as the tool serializes them, so the approval binds to exactly these.</summary>
    internal static string HashArguments<TArgs>(TArgs args) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(args, HostJson.Options)));

    /// <summary>The change list as one line per change, for the audit, the suspension and the approval form.</summary>
    internal static IEnumerable<string> ChangeLines(WritePlan plan) => plan.Changes.Select(c => c.Action switch
    {
        "create" => c.NewValue is null ? $"create {c.Kind} {c.Name}" : $"create {c.Kind} {c.Name} ({c.NewValue})",
        "delete" => c.OldValue is null ? $"delete {c.Kind} {c.Name}" : $"delete {c.Kind} {c.Name} ({c.OldValue})",
        _ when c.Property == "connections" => $"generate connections on {c.Kind} {c.Name} (now {c.OldValue}; may add or remove connections)",
        _ when c.Property == "check messages" => CheckRunLine(plan, c),
        _ when c.Property == "reports" => $"update all reports ({c.OldValue} report pages now); may create, replace or delete report pages",
        _ when c.Property == "report" => $"update report page {c.Name}",
        _ when c.Property == "device tags" => $"renumber {c.OldValue}",
        _ when c.Property == "page number" => $"renumber {c.Kind} {c.Name}",
        _ => $"update {c.Kind} {c.Name}: {c.Property} '{c.OldValue}' -> '{c.NewValue}'",
    });

    /// <summary>A check replaces all the project's check messages; check.run.plan puts its scope in the details.</summary>
    private static string CheckRunLine(WritePlan plan, PlannedChange change)
    {
        string scheme = plan.Details?.GetValueOrDefault("scheme") is { } given ? "scheme: " + given : "EPLAN's current scheme";
        return $"run check on {plan.Details?.GetValueOrDefault("pageCount") ?? "?"} pages ({scheme}), replacing the {change.OldValue} existing check messages";
    }

    private static BridgeError Suspended(WritePlan plan) => new()
    {
        Reason = ErrorReason.WritesSuspended,
        Message = $"Writes to '{plan.Project}' are suspended because the outcome of an earlier write is unknown, and the engineer has not confirmed checking the project. Nothing was changed. Ask the engineer to check the project in EPLAN; the next write call asks for that confirmation again. eplan_session_status shows what is suspended.",
    };

    private static BridgeCallException NotStarted(BridgeError error)
    {
        error.Execution = ExecutionState.NotStarted;
        return new BridgeCallException(error);
    }

    private static JsonObject Record(string kind, string tool, string requestId, WritePlan plan, HandshakeResult eplan) => new()
    {
        ["kind"] = kind,
        ["requestId"] = requestId,
        ["tool"] = tool,
        ["project"] = plan.ProjectPath,
        ["eplan"] = new JsonObject
        {
            ["version"] = eplan.Eplan.Version,
            ["variant"] = eplan.Eplan.Variant,
            ["processId"] = eplan.Eplan.ProcessId,
        },
    };

    /// <summary>
    /// Each suspended write that blocks this one (the project's, the instance's) needs the engineer's confirmation that
    /// the project was checked: a question of its own, before any approval (ADR-0010 section 3). Each answer is recorded;
    /// when all are granted the suspensions are lifted, otherwise WRITES_SUSPENDED.
    /// </summary>
    private async Task ReleaseAsync(string tool, ToolTier tier, string requestId, string argumentsHash, WritePlan plan, HandshakeResult eplan, DateTimeOffset started, IReadOnlyList<Suspension> blocking, CallProgress? call, CancellationToken cancellationToken)
    {
        foreach (Suspension suspension in blocking.DistinctBy(s => s.RequestId))
        {
            ApprovalResult confirmation;
            try
            {
                confirmation = await approvals.ConfirmReleaseAsync(requestId, tool, tier, argumentsHash, plan, eplan.Eplan, suspension, call?.Lane(), cancellationToken).ConfigureAwait(false);
            }
            catch (BridgeCallException ex)
            {
                throw NotStarted(ex.Error);
            }

            JsonObject record = Record("suspension_release", tool, requestId, plan, eplan);
            record["suspendedRequestId"] = suspension.RequestId;
            record["suspendedTool"] = suspension.Tool;
            await RecordDecisionAsync(record, confirmation).ConfigureAwait(false);
            if (confirmation.Decision != ApprovalDecision.Granted)
            {
                throw NotStarted(Suspended(plan));
            }
        }

        suspensions.Release(plan.ProjectPath, eplan.Eplan.ProcessId, started);
        LogReleased(plan.ProjectPath, requestId);
    }

    /// <summary>
    /// The decision as its own record, linked to the execution by the request ID, with the channel and the change list
    /// it was about (architecture 8.8).
    /// </summary>
    private Task RecordApprovalAsync(string tool, string requestId, WritePlan plan, HandshakeResult eplan, ApprovalResult approval)
    {
        JsonObject record = Record("approval", tool, requestId, plan, eplan);
        record["fingerprint"] = plan.Fingerprint;
        record["changes"] = new JsonArray([.. ChangeLines(plan).Select(line => (JsonNode?)line)]);
        return RecordDecisionAsync(record, approval);
    }

    /// <summary>A grant that cannot be recorded is not acted on (AUDIT_UNAVAILABLE); a refusal stands even unrecorded.</summary>
    private async Task RecordDecisionAsync(JsonObject record, ApprovalResult approval)
    {
        record["channel"] = approval.Channel;
        record["decision"] = JsonNamingPolicy.SnakeCaseLower.ConvertName(approval.Decision.ToString());
        if (approval.Decision != ApprovalDecision.Granted)
        {
            await AppendQuietlyAsync(record).ConfigureAwait(false);
            return;
        }

        try
        {
            await audit.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AuditUnavailableException ex)
        {
            LogAuditUnavailable(ex);
            throw NotStarted(new BridgeError { Reason = ErrorReason.AuditUnavailable, Message = AuditUnavailableMessage });
        }
    }

    /// <summary>The result record; for a Destructive write also whether its backup copy was made (ADR-0013).</summary>
    private Task RecordResultAsync(string tool, string requestId, WritePlan plan, HandshakeResult eplan, string execution, string? reason, bool recovered, BackupTarget? backup)
    {
        JsonObject record = Record("execution_result", tool, requestId, plan, eplan);
        record["execution"] = execution;
        record["reason"] = reason;
        record["recovered"] = recovered;
        if (backup is not null)
        {
            // The Bridge changes nothing before the copy exists, so a write that ran had its backup.
            record["backupCompleted"] = reason != ErrorReason.BackupFailed && File.Exists(backup.Path);
        }

        return AppendQuietlyAsync(record);
    }

    /// <summary>
    /// A record after the write ran: when it cannot be written, the "started" record without a result is what a Host
    /// restart looks for (architecture 8.8), so the failure is logged, never thrown over the outcome.
    /// </summary>
    private async Task AppendQuietlyAsync(JsonObject record)
    {
        try
        {
            await audit.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AuditUnavailableException ex)
        {
            LogAuditUnavailable(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The audit log cannot be written")]
    private partial void LogAuditUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Write suspension of {Project} lifted after the engineer's confirmation ({RequestId})")]
    private partial void LogReleased(string project, string requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Backup folder for {Path} failed")]
    private partial void LogBackupFolderFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup folder of {Copy} after a write that did not run: {Now}")]
    private partial void LogBackupFolderSettled(string copy, string now);
}
