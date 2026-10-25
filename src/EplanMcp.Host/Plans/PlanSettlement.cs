using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Hosting;

namespace EplanMcp.Host.Plans;

/// <summary>
/// Folds dead owners' audit facts without restoring tasks, ownership or permission to execute.
/// A per-plan file lock serializes audit publication across Hosts; PlanStore CAS owns the terminal transition.
/// </summary>
internal sealed class PlanSettlement(PlanStore store, AuditLog audit, HostDataLocations locations,
    TimeProvider clock, DanglingWrites dangling) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => SettleAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SettleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dangling.SettleOnceAsync(cancellationToken).ConfigureAwait(false);
            var paths = new PlanPaths(locations.Root);
            if (!Directory.Exists(paths.Root))
            {
                return;
            }
            IReadOnlyList<JsonObject>? snapshot = null;
            foreach (string file in Directory.EnumerateFiles(paths.Root, "*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (!PlanPaths.IsHex(id, 32))
                {
                    continue;
                }
#pragma warning disable CA2000 // The using declaration releases a successful lock on every loop exit, including asynchronous exceptions.
                using FileStream? held = TryLock(paths, id);
#pragma warning restore CA2000
                if (held is null)
                {
                    continue;
                }
                PlanRecord? record;
                try
                {
                    record = await store.ReadAsync(id, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    continue; // Preserve unsupported or damaged records without blocking other accepted plans.
                }
                if (record?.State is not { AttemptId: not null, Status: PlanStatus.Approved or PlanStatus.Running })
                {
                    continue;
                }
                // One audit snapshot serves the whole run; it is read only once a plan may need settling.
                snapshot ??= await audit.ReadAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await SettleAsync(id, record, snapshot, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
                {
                    // A record this fold cannot interpret stays as it is; it must not block other plans or the Host's start.
                }
            }
        }
        catch (Exception ex) when (ex is AuditUnavailableException or IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Settlement is a startup/admission boundary. Keep the record and retry on the next call.
        }
    }

    private async Task SettleAsync(string id, PlanRecord record, IReadOnlyList<JsonObject> snapshot, CancellationToken cancellationToken)
    {
        JsonObject[] entries = [.. snapshot.Where(e => Text(e, "planId") == id)];
        JsonNode? owner = Owner(record, entries);
        // Old records with no process identity cannot prove that their owner exited.
        if (owner is null || DanglingWrites.IsRunning(owner)
            || entries.Any(e => Text(e, "kind") == "execution_started" && DanglingWrites.IsRunning(e["host"])))
        {
            return;
        }
        // The owner's own result, or an earlier settlement's, is authoritative: the owner may have audited it and then
        // exited before its state write, and only its result carries the verification.
        JsonObject? published = entries.LastOrDefault(e => Text(e, "kind") == "plan_result");
        PlanResult? result = published?["result"]?.Deserialize<PlanResult>(PlanJson.Options) ?? Fold(record, entries);
        if (result is null)
        {
            return;
        }
        if (published is null)
        {
            await audit.AppendAsync(new JsonObject
            {
                ["kind"] = "plan_result",
                ["planId"] = id,
                ["settled"] = true,
                ["result"] = JsonSerializer.SerializeToNode(result, PlanJson.Options),
            }, cancellationToken).ConfigureAwait(false);
        }
        _ = await store.UpdateAsync(id, record.State.Revision,
            s => s.Complete(result, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Null means at least one started request still needs DanglingWrites, even if a suspension exists.</summary>
    internal static PlanResult? Fold(PlanRecord record, IReadOnlyList<JsonObject> entries)
    {
        entries = [.. entries.Where(e => Text(e, "planId") == record.Definition.PlanId)];
        var steps = new List<PlanStepResult>();
        var backups = new List<PlanBackup>(record.State.Result?.Backups ?? []);
        foreach (JsonObject copy in entries.Where(e => Text(e, "kind") == "plan_backup"))
        {
            if (copy["backup"] is { } value)
            {
                backups.Add(value.Deserialize<PlanBackup>(PlanJson.Options)!);
            }
        }
        foreach (JsonObject prior in entries.Where(e => Text(e, "kind") == "plan_result"))
        {
            if (prior["result"] is { } value)
            {
                backups.AddRange(value.Deserialize<PlanResult>(PlanJson.Options)!.Backups);
            }
        }
        foreach (PlanStep step in record.Definition.Steps)
        {
            var folded = new PlanStepResult(step.Index, step.Op, step.Effective, ExecutionState.NotStarted);
            foreach (JsonObject started in entries.Where(e => Text(e, "kind") == "execution_started" && Step(e) == step.Index))
            {
                JsonObject? ended = entries.LastOrDefault(e => Text(e, "kind") == "execution_result"
                    && Step(e) == step.Index && Text(e, "requestId") == Text(started, "requestId"));
                if (ended is null)
                {
                    return null;
                }
                string execution = Text(ended, "execution");
                if (execution is not (ExecutionState.Committed or ExecutionState.RolledBack or ExecutionState.NotStarted or ExecutionState.Unknown))
                {
                    return null;
                }
                bool suspended = entries.Any(e => Text(e, "kind") == "writes_suspended" && Step(e) == step.Index
                    && Text(e, "requestId") == Text(started, "requestId"));
                string reason = execution == ExecutionState.Unknown ? ErrorReason.OutcomeUnknown : Text(ended, "reason");
                PlanFailure? error = reason.Length == 0 ? null : new(reason,
                    suspended ? "The interrupted step's outcome is unknown; writes remain suspended."
                        : "The step was settled after its Host exited. Inspect the audit before compiling a new plan.", false);
                string? backup = started["backup"]?.GetValue<string>();
                if (backup is not null)
                {
                    // Outcome metadata does not prove a backup copy usable; retain its location conservatively.
                    backups.Add(new(backup, null, null, false));
                }
                folded = folded with
                {
                    Execution = execution,
                    Error = error,
                    Recovered = execution != ExecutionState.Unknown
                        && (ended["recovered"]?.GetValue<bool>() == true || ended["late"]?.GetValue<bool>() == true),
                    Backup = backup,
                    UndoDescription = execution == ExecutionState.Committed
                        ? string.Create(CultureInfo.InvariantCulture, $"AI: plan {record.Definition.PlanId[..8]} {step.Index}/{record.Definition.Steps.Length} {step.Op}") : null,
                };
            }
            steps.Add(folded);
        }
        return PlanResult.Aggregate(steps) with
        {
            Backups = [.. backups.DistinctBy(b => b.Path, StringComparer.OrdinalIgnoreCase)],
            Verification = "skipped",
            Coverage = JsonSerializer.SerializeToElement(new { reason = "The owner Host exited; no after-evidence is available for verification." }, PlanJson.Options),
        };
    }

    private static JsonNode? Owner(PlanRecord record, JsonObject[] entries) =>
        entries.LastOrDefault(e => Text(e, "kind") == "plan_approval" && Text(e, "ownerHost") == record.State.OwnerHost)?["host"]
        ?? entries.LastOrDefault(e => Text(e, "kind") == "plan_compiled" && e["owner"]?["runId"]?.GetValue<string>() == record.State.OwnerHost)?["host"]
        ?? (record.Definition.Owner.RunId == record.State.OwnerHost
            ? entries.FirstOrDefault(e => Text(e, "kind") == "execution_started")?["host"] : null);

    private static FileStream? TryLock(PlanPaths paths, string id)
    {
        try
        {
            return new(paths.Check(Path.Combine(paths.Root, ".settlement-" + id + ".lock")), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
        {
            return null; // Another Host is publishing this plan's result; its CAS will settle the state.
        }
    }

    // The audit holds every record kind; a field of another shape is simply not a match.
    private static string Text(JsonObject entry, string key) => entry[key] is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;
    private static int? Step(JsonObject entry) => entry["step"] is JsonValue value && value.TryGetValue(out int step) ? step : null;
}
