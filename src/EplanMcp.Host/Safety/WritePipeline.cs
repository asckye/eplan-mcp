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
/// What a write call did: its plan, the execution state, the data of a commit that answered in time, for a committed
/// Destructive write the path of its backup copy, and for a committed write to system master data the path of the
/// database's backup archive (ADR-0019).
/// </summary>
internal sealed record WriteOutcome<TData>(WritePlan Plan, string Execution, TData? Data, bool Recovered, string? Backup = null, string? MasterDataBackup = null);

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

        // A write changes a project, system master data (ADR-0019), or both; one that names neither is a defect.
        MasterDataTarget? masterData = plan.MasterData;
        bool project = plan.ProjectPath.Length > 0;
        if (!project && masterData is null)
        {
            throw NotStarted(new BridgeError { Reason = ErrorReason.Internal, Message = "The plan names nothing to change, so nothing was changed." });
        }

        // Nobody is asked anything about a write the policy forbids anyway.
        PolicyDecision decision = PolicyEngine.Decide(request.Tier, !project || ScratchPaths.IsInside(plan.ProjectPath, config.ScratchRoots), config, masterData);
        if (decision.Verdict == PolicyVerdict.Deny)
        {
            throw NotStarted(new BridgeError { Reason = ErrorReason.PolicyDenied, Message = decision.Message! });
        }

        // The Host names the write, never a client: the approval and the execution are bound to this ID.
        string requestId = Guid.NewGuid().ToString("N");
        string argumentsHash = HashArguments(request.Args);
        if (suspensions.Blocking(plan.ProjectPath, eplan.Eplan.ProcessId, started, masterData) is { Count: > 0 } blocking)
        {
            await ReleaseAsync(request.Tool, request.Tier, requestId, argumentsHash, plan, eplan, started, blocking, call, cancellationToken).ConfigureAwait(false);
        }

        bool destructive = request.Tier == ToolTier.Destructive && project;
        ApprovalResult? approval = null;
        if (decision.Verdict == PolicyVerdict.NeedsApproval)
        {
            try
            {
                string? backupFolder = destructive ? backups.ProjectFolder(plan) : null;
                string? masterDataBackups = masterData is null ? null : backups.MasterDataFolder;
                approval = await approvals.AskAsync(requestId, request.Tool, request.Tier, argumentsHash, plan, eplan.Eplan, backupFolder, masterDataBackups, call?.Lane(), cancellationToken).ConfigureAwait(false);
            }
            catch (BridgeCallException ex)
            {
                throw NotStarted(ex.Error);
            }
        }

        // The window that granted the write shows what became of it (ADR-0014), and is let go when the call ends.
        using IApprovalOutcome? shown = approval?.Outcome;
        BackupTarget? backup = null;
        string? masterDataFolder = null;
        try
        {
            if (approval is not null)
            {
                await RecordApprovalAsync(request.Tool, requestId, plan, eplan, approval).ConfigureAwait(false);
                if (approval.Decision != ApprovalDecision.Granted)
                {
                    throw NotStarted(approvals.Refusal(approval.Decision));
                }
            }

            // Destructive and master-data writes were approved above in any case (PolicyEngine); their backup folders
            // are made only after that.
            backup = destructive ? NewBackup(plan, request.Limit) : null;
            masterDataFolder = masterData is null ? null : NewMasterDataFolder();
        }
        catch (BridgeCallException ex) when (backup is not null)
        {
            Settle(backup, ExecutionState.NotStarted, null);
            await Show(shown, ExecutionState.NotStarted, ex.Error.Reason).ConfigureAwait(false);
            throw;
        }
        catch (BridgeCallException ex)
        {
            await Show(shown, ExecutionState.NotStarted, ex.Error.Reason).ConfigureAwait(false);
            throw;
        }

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

        if (masterDataFolder is not null)
        {
            record["masterDataBackupFolder"] = masterDataFolder;
        }

        try
        {
            await audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (AuditUnavailableException ex)
        {
            LogAuditUnavailable(ex);
            Settle(backup, ExecutionState.NotStarted, null);
            SettleMasterData(masterDataFolder, null);
            await Show(shown, ExecutionState.NotStarted, ErrorReason.AuditUnavailable).ConfigureAwait(false);
            throw NotStarted(new BridgeError { Reason = ErrorReason.AuditUnavailable, Message = AuditUnavailableMessage });
        }

        // The master data is backed up before the write is sent (ADR-0019); a failure there is the write's result,
        // not_started. The Bridge has not seen the write's request ID yet, so a Host crash here leaves a write that
        // outcome.get reports as never started.
        string? masterDataBackup = null;
        if (masterDataFolder is not null)
        {
            try
            {
                masterDataBackup = await BackUpMasterDataAsync(plan.MasterData!, masterDataFolder, request.Limit).ConfigureAwait(false);
            }
            catch (BridgeCallException ex)
            {
                await RecordResultAsync(request.Tool, requestId, plan, eplan, ExecutionState.NotStarted, ex.Error.Reason, recovered: false, backup, null).ConfigureAwait(false);
                Settle(backup, ExecutionState.NotStarted, null);
                SettleMasterData(masterDataFolder, ex.Error);
                await Show(shown, ExecutionState.NotStarted, ex.Error.Reason).ConfigureAwait(false);
                throw;
            }
        }

        // From here on the write may run: no cancellation may skip its result record. A cancel stops it in the Bridge
        // at its next safe point; what became of it is recorded all the same (M4 design 4.3).
        using BridgeProgress? waiting = BridgeProgress.Start(call, request.Activity, clock, cancellationToken);
        try
        {
            WriteResult<TData> result = await bridge.InvokeWriteAsync<TExecuteArgs, TData>(request.ExecuteOperation, request.ExecuteArgs(plan, backup), requestId, request.Limit, waiting, cancellationToken).ConfigureAwait(false);
            await RecordResultAsync(request.Tool, requestId, plan, eplan, ExecutionState.Committed, null, result.Recovered, backup, masterDataBackup).ConfigureAwait(false);
            await Show(shown, ExecutionState.Committed, null).ConfigureAwait(false);
            return new WriteOutcome<TData>(plan, ExecutionState.Committed, result.Data, result.Recovered, backup?.Path, masterDataBackup);
        }
        catch (BridgeCallException ex)
        {
            string execution = ex.Error.Execution ?? ExecutionState.Unknown;
            await RecordResultAsync(request.Tool, requestId, plan, eplan, execution, ex.Error.Reason, recovered: false, backup, masterDataBackup).ConfigureAwait(false);
            if (execution == ExecutionState.Unknown)
            {
                suspensions.Suspend(plan.ProjectPath, eplan.Eplan.ProcessId, started, request.Tool, requestId, clock.GetUtcNow(), SuspensionReason.OutcomeUnknown, ChangeLines(plan), masterData);
                await AppendQuietlyAsync(Record("writes_suspended", request.Tool, requestId, plan, eplan)).ConfigureAwait(false);
            }

            Settle(backup, execution, ex.Error);
            if (masterDataBackup is not null && execution != ExecutionState.NotStarted)
            {
                // The database cannot be rolled back (S24), so its backup is what the engineer restores: 'backup'; a
                // project copy made as well is 'projectBackup' (docs/tools/eplan_part_sync.md, section 3).
                ex.Error.Metadata ??= [];
                if (ex.Error.Metadata.Remove("backup", out string? projectCopy))
                {
                    ex.Error.Metadata["projectBackup"] = projectCopy;
                }

                ex.Error.Metadata["backup"] = masterDataBackup;
            }
            else
            {
                SettleMasterData(masterDataFolder, ex.Error);
            }

            await Show(shown, execution, ex.Error.Reason).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Shows the write's outcome in the window that granted it, if any (ADR-0014); never fails the write.</summary>
    private static Task Show(IApprovalOutcome? shown, string execution, string? reason) =>
        shown?.ReportAsync(execution, reason) ?? Task.CompletedTask;

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

    /// <summary>A new folder for the master-data backup (<see cref="BackupFolders.CreateMasterData"/>); BACKUP_FAILED when it cannot be made.</summary>
    private string NewMasterDataFolder()
    {
        try
        {
            return backups.CreateMasterData();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogBackupFolderFailed(ex, backups.MasterDataFolder);
            throw new BridgeCallException(BackupFolders.MasterDataFailure());
        }
    }

    /// <summary>
    /// Has EPLAN back the planned database up into <paramref name="folder"/> (masterdata.backup, ADR-0019) within half
    /// the execution limit and returns the archive, which must lie in the folder and not be empty. Every failure is
    /// BACKUP_FAILED, not_started, except what the Bridge found before running the backup: a database on SQL Server,
    /// another database configured meanwhile, or EPLAN busy. Not cancelled with the call: it ends within its limit.
    /// </summary>
    private async Task<string> BackUpMasterDataAsync(MasterDataTarget target, string folder, TimeSpan limit)
    {
        var args = new MasterDataBackupArgs { Kind = target.Kind, Database = target.Database, Folder = folder, ArchiveName = target.Kind, MaxSeconds = (int)(limit.TotalSeconds / 2) };
        MasterDataBackupData data;
        try
        {
            data = await bridge.InvokeAsync<MasterDataBackupArgs, MasterDataBackupData>(OperationNames.MasterDataBackup, args, TimeSpan.FromSeconds(args.MaxSeconds) + EplanDialogChannel.Slack, null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            LogMasterDataBackupFailed(folder, ex.Error.Reason, ex.Error.Message);
            BridgeError error = ex.Error.Reason is ErrorReason.BackupFailed or ErrorReason.PolicyDenied or ErrorReason.PlanStale or ErrorReason.EplanBusy
                ? ex.Error
                : BackupFolders.MasterDataFailure();
            throw NotStarted(error);
        }

        var archive = new FileInfo(data.Archive);
        if (!string.Equals(archive.DirectoryName, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !archive.Exists || archive.Length == 0)
        {
            LogMasterDataBackupFailed(folder, ErrorReason.BackupFailed, "the archive is not a non-empty file in the folder");
            throw NotStarted(BackupFolders.MasterDataFailure());
        }

        LogMasterDataBackedUp(target.Database, archive.FullName, archive.Length);
        return archive.FullName;
    }

    /// <summary>After a write that did not run, the master-data backup folder goes the way of a project's (<see cref="BackupFolders.SettleFolder"/>).</summary>
    private void SettleMasterData(string? folder, BridgeError? error)
    {
        if (folder is null)
        {
            return;
        }

        try
        {
            string? kept = BackupFolders.SettleFolder(folder, failed: error?.Reason == ErrorReason.BackupFailed);
            LogBackupFolderSettled(folder, kept ?? "removed, empty");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogBackupFolderFailed(ex, folder);
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
        // part.import.plan: the parts a file adds to or updates in the parts database, counted (ADR-0019).
        "create" when c.Kind == "parts" => $"add {Parts(c.NewValue)} from {c.Name} to the parts database",
        _ when c.Kind == "parts" => $"update {Parts(c.NewValue)} of the parts database from {c.Name}",

        // part.sync.plan: the referenced parts EPLAN stores in the project as well (S24 r15).
        _ when c.Property == "stored parts" => $"store {Parts(c.NewValue)} the project references in project {c.Name} (now {c.OldValue})",
        "create" => c.NewValue is null ? $"create {c.Kind} {c.Name}" : $"create {c.Kind} {c.Name} ({c.NewValue})",
        "delete" => c.OldValue is null ? $"delete {c.Kind} {c.Name}" : $"delete {c.Kind} {c.Name} ({c.OldValue})",
        _ when c.Property == "connections" => $"generate connections on {c.Kind} {c.Name} (now {c.OldValue}; may add or remove connections)",
        _ when c.Property == "check messages" => CheckRunLine(plan, c),
        _ when c.Property == "reports" => $"update all reports ({c.OldValue} report pages now); may create, replace or delete report pages",
        _ when c.Property == "report" => $"update report page {c.Name}",
        _ when c.Property == "device tags" => $"renumber {c.OldValue}",
        _ when c.Property == "page number" => $"renumber {c.Kind} {c.Name}",
        _ when c.Property == "numbering" => $"renumber {c.OldValue}",
        _ when c.Property == "translations" => $"translate {c.OldValue}",
        _ when c.Property == "value set" => $"apply value set '{c.NewValue}' to placeholder {c.Name}",
        _ => $"update {c.Kind} {c.Name}: {c.Property} '{c.OldValue}' -> '{c.NewValue}'",
    });

    private static string Parts(string? count) => count == "1" ? "1 part" : (count ?? "?") + " parts";

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

    /// <summary>A record about the write; one that changes system master data also names the database (ADR-0019).</summary>
    private static JsonObject Record(string kind, string tool, string requestId, WritePlan plan, HandshakeResult eplan)
    {
        var record = new JsonObject
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
        if (plan.MasterData is { } target)
        {
            record["masterData"] = new JsonObject
            {
                ["kind"] = target.Kind,
                ["database"] = target.Database,
                ["path"] = target.Path,
                ["server"] = target.Server,
            };
        }

        return record;
    }

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

        suspensions.Release(plan.ProjectPath, eplan.Eplan.ProcessId, started, plan.MasterData);
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

    /// <summary>
    /// The result record; for a Destructive write also whether its backup copy was made (ADR-0013), and for a write to
    /// system master data the database's backup archive, once made (ADR-0019).
    /// </summary>
    private Task RecordResultAsync(string tool, string requestId, WritePlan plan, HandshakeResult eplan, string execution, string? reason, bool recovered, BackupTarget? backup, string? masterDataBackup)
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

        if (masterDataBackup is not null)
        {
            record["masterDataBackup"] = masterDataBackup;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Master data {Database} backed up to {Archive} ({Bytes} bytes)")]
    private partial void LogMasterDataBackedUp(string database, string archive, long bytes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Master-data backup into {Folder} failed: {Reason} {Problem}")]
    private partial void LogMasterDataBackupFailed(string folder, string reason, string problem);
}
