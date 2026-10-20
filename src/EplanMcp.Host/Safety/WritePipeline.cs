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
/// whether it is a dry run, and how long it may run (M3 design 4.3, 4.4).
/// </summary>
internal sealed record WriteRequest<TArgs, TExecuteArgs>(
    string Tool,
    ToolTier Tier,
    string PlanOperation,
    string ExecuteOperation,
    TArgs Args,
    Func<WritePlan, TExecuteArgs> ExecuteArgs,
    bool DryRun,
    TimeSpan Limit);

/// <summary>What a write call did: its plan, the execution state, and the data of a commit that answered in time.</summary>
internal sealed record WriteOutcome<TData>(WritePlan Plan, string Execution, TData? Data, bool Recovered);

/// <summary>
/// The steps every write tool goes through (M3 design 3, ADR-0005, ADR-0010): plan, dry run, policy, the confirmation
/// that lifts a suspension, approval, the "started" audit record, one execute that is never sent twice, the result
/// record, and a suspension when the outcome is unknown. Every error it throws carries the execution state.
/// </summary>
internal sealed partial class WritePipeline(
    IBridgeClient bridge,
    HostConfig config,
    SuspensionStore suspensions,
    AuditLog audit,
    DanglingWrites dangling,
    ApprovalService approvals,
    TimeProvider clock,
    ILogger<WritePipeline> logger)
{
    /// <summary>Runs <paramref name="request"/> without reporting progress.</summary>
    public Task<WriteOutcome<TData>> RunAsync<TArgs, TExecuteArgs, TData>(WriteRequest<TArgs, TExecuteArgs> request, CancellationToken cancellationToken) =>
        RunAsync<TArgs, TExecuteArgs, TData>(request, progress: null, cancellationToken);

    /// <summary>
    /// Runs <paramref name="request"/>. <paramref name="progress"/> is the tool's <c>IProgress</c> parameter, which the
    /// SDK binds to the client's progress token: while the engineer is asked, the wait is reported there (ADR-0011).
    /// </summary>
    public async Task<WriteOutcome<TData>> RunAsync<TArgs, TExecuteArgs, TData>(WriteRequest<TArgs, TExecuteArgs> request, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
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
            await ReleaseAsync(request.Tool, requestId, argumentsHash, plan, eplan, started, blocking, progress, cancellationToken).ConfigureAwait(false);
        }

        if (decision.Verdict == PolicyVerdict.NeedsApproval)
        {
            ApprovalResult approval;
            try
            {
                approval = await approvals.AskAsync(requestId, request.Tool, argumentsHash, plan, progress, cancellationToken).ConfigureAwait(false);
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

        JsonObject record = Record("execution_started", request.Tool, requestId, plan, eplan);
        record["eplan"]!["start"] = started.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        record["host"] = DanglingWrites.ThisHost();
        record["tier"] = request.Tier.ToString();
        record["policy"] = decision.Verdict.ToString();
        record["arguments"] = JsonSerializer.SerializeToNode(request.Args, HostJson.Options);
        record["changes"] = new JsonArray([.. ChangeLines(plan).Select(line => (JsonNode?)line)]);
        try
        {
            await audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (AuditUnavailableException ex)
        {
            LogAuditUnavailable(ex);
            throw NotStarted(new BridgeError { Reason = ErrorReason.AuditUnavailable, Message = AuditUnavailableMessage });
        }

        // From here on the write may run: no cancellation may skip its result record.
        try
        {
            WriteResult<TData> result = await bridge.InvokeWriteAsync<TExecuteArgs, TData>(request.ExecuteOperation, request.ExecuteArgs(plan), requestId, request.Limit, cancellationToken).ConfigureAwait(false);
            await RecordResultAsync(request.Tool, requestId, plan, eplan, ExecutionState.Committed, null, result.Recovered).ConfigureAwait(false);
            return new WriteOutcome<TData>(plan, ExecutionState.Committed, result.Data, result.Recovered);
        }
        catch (BridgeCallException ex)
        {
            string execution = ex.Error.Execution ?? ExecutionState.Unknown;
            await RecordResultAsync(request.Tool, requestId, plan, eplan, execution, ex.Error.Reason, recovered: false).ConfigureAwait(false);
            if (execution == ExecutionState.Unknown)
            {
                suspensions.Suspend(plan.ProjectPath, eplan.Eplan.ProcessId, started, request.Tool, requestId, clock.GetUtcNow(), SuspensionReason.OutcomeUnknown, ChangeLines(plan));
                await AppendQuietlyAsync(Record("writes_suspended", request.Tool, requestId, plan, eplan)).ConfigureAwait(false);
            }

            throw;
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
        _ when c.Property == "connections" => $"generate connections on {c.Kind} {c.Name} (now {c.OldValue}; may add or remove connections)",
        _ => $"update {c.Kind} {c.Name}: {c.Property} '{c.OldValue}' -> '{c.NewValue}'",
    });

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
    private async Task ReleaseAsync(string tool, string requestId, string argumentsHash, WritePlan plan, HandshakeResult eplan, DateTimeOffset started, IReadOnlyList<Suspension> blocking, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        foreach (Suspension suspension in blocking.DistinctBy(s => s.RequestId))
        {
            ApprovalResult confirmation;
            try
            {
                confirmation = await approvals.ConfirmReleaseAsync(requestId, tool, argumentsHash, plan, suspension, progress, cancellationToken).ConfigureAwait(false);
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

    private Task RecordResultAsync(string tool, string requestId, WritePlan plan, HandshakeResult eplan, string execution, string? reason, bool recovered)
    {
        JsonObject record = Record("execution_result", tool, requestId, plan, eplan);
        record["execution"] = execution;
        record["reason"] = reason;
        record["recovered"] = recovered;
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
}
