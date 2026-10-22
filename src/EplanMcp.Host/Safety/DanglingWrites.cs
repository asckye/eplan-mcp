using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Safety;

/// <summary>
/// After a Host crash (ADR-0010 section 2): an "execution_started" record without a result, left by a Host process
/// that has exited, is a write whose outcome nobody reported. Before this Host's first write, each one is settled: the
/// outcome is asked of the same EPLAN instance and recorded late; when it cannot be learned, or the write is still
/// running, the project and the instance are suspended. Records of a Host still running are its writes in progress.
/// </summary>
internal sealed partial class DanglingWrites(IBridgeClient bridge, SuspensionStore suspensions, AuditLog audit, TimeProvider clock, ILogger<DanglingWrites> logger)
{
    private readonly object _gate = new();
    private Task? _settling;

    /// <summary>The identity of this Host process, written into every "execution_started" record.</summary>
    public static JsonObject ThisHost() => new()
    {
        ["processId"] = Environment.ProcessId,
        ["start"] = SuspensionStore.StartOf(Environment.ProcessId)?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Settles the dangling writes once per Host process: callers share one run, later calls return at once. A run
    /// that failed (the audit log could not be written) is tried again by the next call.
    /// </summary>
    public async Task SettleOnceAsync(CancellationToken cancellationToken)
    {
        Task settling;
        lock (_gate)
        {
            settling = _settling ??= SettleAsync(CancellationToken.None);
        }

        try
        {
            await settling.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_settling, settling) && settling.IsFaulted)
                {
                    _settling = null;
                }
            }

            throw;
        }
    }

    private async Task SettleAsync(CancellationToken cancellationToken)
    {
        foreach (JsonObject started in audit.Dangling().Where(r => !IsRunning(r["host"])))
        {
            string requestId = Text(started, "requestId");
            string project = Text(started, "project");
            string tool = Text(started, "tool");
            int processId = started["eplan"]?["processId"]?.GetValue<int>() ?? 0;
            DateTimeOffset? eplanStart = Time(started["eplan"]?["start"]);
            bool eplanRuns = eplanStart is { } start && SuspensionStore.StartOf(processId) == start;

            OutcomeGetData? outcome = eplanRuns ? await bridge.FetchOutcomeAsync(processId, requestId, cancellationToken).ConfigureAwait(false) : null;
            string execution = outcome?.Status is OutcomeStatus.Committed or OutcomeStatus.RolledBack or OutcomeStatus.NotStarted
                ? outcome.Status
                : ExecutionState.Unknown;
            LogSettled(requestId, outcome?.Status ?? (eplanRuns ? "unreachable" : "EPLAN gone"), execution);

            JsonObject result = Settled("execution_result", started);
            result["execution"] = execution;
            result["reason"] = outcome?.Reason;
            result["late"] = true;
            await audit.AppendAsync(result, cancellationToken).ConfigureAwait(false);
            if (execution == ExecutionState.Unknown)
            {
                SuspensionReason reason = outcome?.Status is OutcomeStatus.Queued or OutcomeStatus.Running ? SuspensionReason.StillRunning : SuspensionReason.OutcomeUnknown;
                IEnumerable<string> changes = started["changes"] is JsonArray lines ? lines.Select(l => l?.GetValue<string>() ?? string.Empty) : [];
                suspensions.Suspend(project, processId, eplanStart ?? DateTimeOffset.MinValue, tool, requestId, clock.GetUtcNow(), reason, changes, MasterDataOf(started));
                await audit.AppendAsync(Settled("writes_suspended", started), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static JsonObject Settled(string kind, JsonObject started)
    {
        var record = new JsonObject
        {
            ["kind"] = kind,
            ["requestId"] = started["requestId"]?.DeepClone(),
            ["tool"] = started["tool"]?.DeepClone(),
            ["project"] = started["project"]?.DeepClone(),
            ["eplan"] = started["eplan"]?.DeepClone(),
        };
        if (started["masterData"] is { } masterData)
        {
            record["masterData"] = masterData.DeepClone();
        }

        return record;
    }

    /// <summary>Whether the Host that wrote a record still runs; a record without a readable identity counts as exited.</summary>
    private static bool IsRunning(JsonNode? host) =>
        host?["processId"] is JsonValue id && id.TryGetValue(out int processId)
        && Time(host["start"]) is { } start
        && SuspensionStore.StartOf(processId) == start;

    private static DateTimeOffset? Time(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text)
        && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset time)
            ? time
            : null;

    private static string Text(JsonObject record, string name) =>
        record[name] is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;

    /// <summary>The system master data a record names (ADR-0019), whose suspension is keyed by it; null when it names none.</summary>
    private static MasterDataTarget? MasterDataOf(JsonObject record) => record["masterData"] is JsonObject target
        ? new MasterDataTarget
        {
            Kind = Text(target, "kind"),
            Database = Text(target, "database"),
            Path = Text(target, "path"),
            Server = target["server"] is JsonValue server && server.TryGetValue(out bool value) && value,
        }
        : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dangling write {RequestId} settled: {Status}, recorded as {Execution}")]
    private partial void LogSettled(string requestId, string status, string execution);
}
