using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging;

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
/// The steps every write tool goes through (M3 design 3, ADR-0005, ADR-0010): suspension check, plan, dry run,
/// policy, approval, the "started" audit record, one execute that is never sent twice, the result record, and a
/// suspension when the outcome is unknown. Every error it throws carries the execution state.
/// </summary>
internal sealed partial class WritePipeline(
    IBridgeClient bridge,
    HostConfig config,
    SuspensionStore suspensions,
    AuditLog audit,
    DanglingWrites dangling,
    TimeProvider clock,
    ILogger<WritePipeline> logger)
{
    public async Task<WriteOutcome<TData>> RunAsync<TArgs, TExecuteArgs, TData>(WriteRequest<TArgs, TExecuteArgs> request, CancellationToken cancellationToken)
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

        if (suspensions.Blocking(plan.ProjectPath, eplan.Eplan.ProcessId, started).Count > 0)
        {
            // Releasing needs the engineer's confirmation through an approval channel (ADR-0010 section 3), M3 step 8.
            throw NotStarted(Suspended(plan));
        }

        PolicyDecision decision = PolicyEngine.Decide(request.Tier, ScratchPaths.IsInside(plan.ProjectPath, config.ScratchRoots), config);
        if (decision.Verdict == PolicyVerdict.Deny)
        {
            throw NotStarted(new BridgeError { Reason = ErrorReason.PolicyDenied, Message = decision.Message! });
        }

        if (decision.Verdict == PolicyVerdict.NeedsApproval)
        {
            // No approval channel yet (M3 step 8): with none available, whatever needs approval is refused (ADR-0005).
            throw NotStarted(new BridgeError { Reason = ErrorReason.ApprovalDenied, Message = NoApprovalChannel });
        }

        string requestId = Guid.NewGuid().ToString("N");
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

    internal const string NoApprovalChannel =
        "This write needs the engineer's approval, and no approval channel is available in this version, so it was not run. Nothing was changed.";

    internal const string AuditUnavailableMessage =
        "The audit log cannot be written, so writes are refused and nothing was changed. Ask the engineer to run 'eplanmcp doctor'.";

    /// <summary>The change list as one line per change, for the audit, the suspension and the approval form.</summary>
    internal static IEnumerable<string> ChangeLines(WritePlan plan) => plan.Changes.Select(c => c.Action switch
    {
        "create" => $"create {c.Kind} {c.Name}",
        _ => $"update {c.Kind} {c.Name}: {c.Property} '{c.OldValue}' -> '{c.NewValue}'",
    });

    private static BridgeError Suspended(WritePlan plan) => new()
    {
        Reason = ErrorReason.WritesSuspended,
        Message = $"Writes to '{plan.Project}' are suspended because the outcome of an earlier write is unknown. Ask the engineer to check the project in EPLAN; eplan_session_status shows what is suspended. Nothing was changed.",
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
}
