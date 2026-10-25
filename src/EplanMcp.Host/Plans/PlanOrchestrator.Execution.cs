using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Plans;

internal sealed partial class PlanOrchestrator
{
    public async Task<PlanRecord> ExecuteAsync(McpServer server, PlanPreparation preparation,
        CallProgress? progress, CancellationToken cancellationToken)
    {
        if (!preparation.TryExecute())
        {
            throw Failure(ErrorReason.InvalidArgument, ReplayMessage);
        }
        using (preparation)
        {
            PlanRecord record = preparation.Record;
            PlanDefinition plan = record.Definition;
            if (!trust.Owns(plan.PlanId, tasks.OwnerFor(server)) || preparation.Task is null || preparation.ProjectLock is null
                || record.State.Status != PlanStatus.Running || tasks.Get(server, preparation.Task.TaskId).IsTerminal)
            {
                throw Failure(ErrorReason.InvalidArgument, ReplayMessage);
            }

            PlanStepResult[] steps = [.. plan.Steps.Select(s => new PlanStepResult(s.Index, s.Op, s.Effective, ExecutionState.NotStarted))];
            var evidence = new List<PlanStepEvidence>();
            var references = new Dictionary<string, string>(StringComparer.Ordinal);
            var backups = new List<PlanBackup>();
            if (preparation.Backup is { } initial)
            {
                backups.Add(initial);
            }
            PlanEvidence? baseline = null;
            PlanFailure? error = null;
            PlanVerification? verification = null;
            bool local = plan.CompilerVersion == "2";
            var observedPages = new Dictionary<int, string[]>();
            string? collecting = null;
            int currentStep = -1;
            try
            {
                PlanRecord? stored = await store.ReadAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
                if (stored?.State != record.State || stored.PlanHash != record.PlanHash
                    || preparation.Backup is not { Usable: true } copy || !File.Exists(copy.Path))
                {
                    throw PlanApplyChecks.Stale();
                }
                byte[] bytes = await store.ReadEvidenceAsync(plan.PlanId, plan.ContextRef!["sha256:".Length..], cancellationToken).ConfigureAwait(false);
                PlanSnapshot approved = JsonSerializer.Deserialize<PlanBaseline>(bytes, PlanJson.Options)!.Snapshot;
                baseline = new(local ? approved with { SnapshotHash = null } : approved, !local);
                foreach ((PlanStep step, int position) in plan.Steps.Select((s, i) => (s, i)))
                {
                    currentStep = step.Index;
                    if (!step.Effective)
                    {
                        PlanEvidence unchanged = evidence.LastOrDefault()?.After ?? baseline;
                        evidence.Add(new(step.Index, ExecutionState.NotStarted, unchanged, unchanged, true));
                        continue;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    string folder = packs.Workspaces.FolderOf(plan.Project.Path);
                    Directory.CreateDirectory(folder);
#pragma warning disable CA2000 // The using block releases the binding lock on every loop exit, including asynchronous exceptions.
                    using (var binding = new FileStream(Path.Combine(folder, ".binding.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
#pragma warning restore CA2000
                    {
                        _ = await new PlanApplyChecks(bridge, packs, store, trust).CheckAsync(record, true, cancellationToken).ConfigureAwait(false);
                        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                        if (status.Handshake?.Eplan.ProcessId != preparation.Instance!.ProcessId
                            || !status.Instances.Any(i => i.PipeName == preparation.Pipe && i.ProcessId == preparation.Instance.ProcessId)
                            || SuspensionStore.StartOf(preparation.Instance.ProcessId) != preparation.InstanceStart
                            || new[] { OperationNames.PlanStepExecute, OperationNames.OutcomeGet }.Except(status.Handshake.Operations ?? [], StringComparer.Ordinal).Any())
                        {
                            throw PlanApplyChecks.Stale();
                        }
                        if (suspensions.Blocking(plan.Project.Path, preparation.Instance.ProcessId, preparation.InstanceStart!.Value).Count > 0)
                        {
                            throw Failure(ErrorReason.WritesSuspended, "Writes became suspended; inspect the earlier write before compiling again.");
                        }

                        PlanEvidence before = await ObserveAsync(step, "before", cancellationToken).ConfigureAwait(false);
                        evidence.Add(new(step.Index, ExecutionState.NotStarted, before, null, true));
                        WritePlan fresh = await new PlanStepCompiler(bridge).PlanAsync(plan.Project.Path,
                            new(step.Index, step.Op, step.Args, step.Tier, step.DependsOn), cancellationToken).ConfigureAwait(false);
                        PlanMacroInspectData? macro = step.Op == "macro.insert"
                            ? await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect,
                                new() { Macro = step.Args.GetProperty("macro").GetString() }, cancellationToken).ConfigureAwait(false) : null;
                        if (step.Prediction is null ? !PlanApplyChecks.Matches(plan, step, fresh)
                            : !PlanPredictionMatch.Matches(step, fresh, step.Args, macro, macro?.Functions.Select(f => new PlanMacroFunction
                            { Kind = f.Kind, Category = f.Category, Main = f.Main, Name = PlanNamePattern.Name(step.Args.GetProperty("page").GetString()!, f), VisibleName = f.VisibleName }).ToArray()))
                        {
                            throw PlanApplyChecks.Stale();
                        }
                        PlanStepExecuteArgs args = PlanStepRequest.Build(plan, step, fresh, evidence, baseline, macro);
                        if (macro is not null && !args.Guard.ResourceHashes.Contains(macro.Sha256, StringComparer.Ordinal))
                        {
                            throw PlanApplyChecks.Stale();
                        }
                        TimeSpan limit = step.Op == "device.renumber" ? DeviceDestructiveTools.Limit
                            : step.Args.TryGetProperty("pages", out JsonElement pages) ? PageWriteTools.Limit(pages.GetArrayLength()) : PageWriteTools.DefaultLimit;
                        RequireTime(limit);
                        if (step.Tier == ToolTier.Destructive)
                        {
                            try
                            {
                                args.Backup = new() { Path = folders.Create(plan.Project.Path), MaxSeconds = (int)limit.TotalSeconds / 2 };
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                throw new BridgeCallException(BackupFolders.Failure());
                            }
                            backups.Add(new(args.Backup.Path, null, null, false));
                        }
                        string requestId = Guid.NewGuid().ToString("N");
                        JsonObject entry = new()
                        {
                            ["kind"] = "execution_started",
                            ["planId"] = plan.PlanId,
                            ["step"] = step.Index,
                            ["requestId"] = requestId,
                            ["project"] = plan.Project.Path,
                            ["tool"] = "eplan_plan_apply",
                            ["host"] = DanglingWrites.ThisHost(),
                            ["eplan"] = new JsonObject { ["processId"] = preparation.Instance.ProcessId, ["start"] = preparation.InstanceStart.Value.ToString("O", CultureInfo.InvariantCulture) },
                            ["argumentsHash"] = PlanStore.Hash(Encoding.UTF8.GetBytes(PlanCanonical.Serialize(step.Args))),
                            ["readScope"] = JsonSerializer.SerializeToNode(step.ReadScope, PlanJson.Options),
                            ["modifyScope"] = JsonSerializer.SerializeToNode(step.ModifyScope, PlanJson.Options),
                            ["sources"] = JsonSerializer.SerializeToNode(plan.Sources, PlanJson.Options),
                            ["resourceHashes"] = JsonSerializer.SerializeToNode(args.Guard.ResourceHashes, PlanJson.Options),
                            ["changes"] = new JsonArray([.. WriteChangeLines.For(fresh).Select(l => (JsonNode?)l)]),
                            ["backup"] = args.Backup?.Path,
                        };
                        await audit.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        RequireTime(limit);
                        try
                        {
                            using BridgeProgress? reporting = BridgeProgress.Start(progress, "Executing plan step", packs.Clock, cancellationToken);
                            WriteResult<JsonElement> reply = await bridge.InvokeWriteAsync<PlanStepExecuteArgs, JsonElement>(OperationNames.PlanStepExecute,
                                args, requestId, limit, reporting, cancellationToken).ConfigureAwait(false);
                            steps[position] = steps[position] with { Execution = ExecutionState.Committed, Recovered = reply.Recovered };
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            BridgeCallException known = ex as BridgeCallException ?? new(BridgeErrors.OutcomeUnknown());
                            steps[position] = steps[position] with
                            {
                                Execution = known.Error.Execution ?? ExecutionState.Unknown,
                                Error = ToFailure(known.Error.Execution is null ? new BridgeCallException(BridgeErrors.OutcomeUnknown()) : known)
                            };
                        }
                        PlanStepResult settled = steps[position] with
                        {
                            // Only a committed step leaves an undo step in EPLAN.
                            UndoDescription = steps[position].Execution == ExecutionState.Committed
                                ? string.Create(CultureInfo.InvariantCulture, $"AI: plan {plan.PlanId[..8]} {step.Index}/{plan.Steps.Length} {step.Op}")
                                : null,
                            Backup = args.Backup?.Path,
                        };
                        steps[position] = settled;
                        evidence[^1] = evidence[^1] with { Execution = settled.Execution };
                        if (settled.Execution == ExecutionState.Unknown)
                        {
                            suspensions.Suspend(plan.Project.Path, preparation.Instance.ProcessId, preparation.InstanceStart.Value,
                                "eplan_plan_apply", requestId, packs.Clock.GetUtcNow(), SuspensionReason.OutcomeUnknown, WriteChangeLines.For(fresh));
                            try
                            {
                                await audit.AppendAsync(new JsonObject
                                {
                                    ["kind"] = "writes_suspended",
                                    ["planId"] = plan.PlanId,
                                    ["step"] = step.Index,
                                    ["requestId"] = requestId,
                                    ["tool"] = "eplan_plan_apply",
                                    ["project"] = plan.Project.Path,
                                    ["eplan"] = entry["eplan"]!.DeepClone(),
                                }, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (AuditUnavailableException)
                            {
                                // The suspension store already holds the suspension, and the plan result reports the unknown step.
                            }
                        }
                        if (args.Backup is not null)
                        {
                            if (settled.Execution == ExecutionState.NotStarted)
                            {
                                string? failed = BackupFolders.Settle(args.Backup.Path, settled.Error?.Reason == ErrorReason.BackupFailed);
                                backups[^1] = backups[^1] with { Path = failed ?? args.Backup.Path };
                            }
                            else
                            {
                                backups[^1] = backups[^1] with { Usable = settled.Execution != ExecutionState.Unknown && File.Exists(args.Backup.Path) };
                            }
                        }
                        entry["kind"] = "execution_result";
                        entry["execution"] = settled.Execution;
                        entry["reason"] = settled.Error?.Reason;
                        entry["recovered"] = settled.Recovered;
                        await audit.AppendAsync(entry, CancellationToken.None).ConfigureAwait(false);
                        tasks.Update(server, preparation.Task.TaskId, new("Step settled", steps.Count(s => s.Execution == ExecutionState.Committed), preparation.Task.Progress.TotalSteps));
                        evidence[^1] = evidence[^1] with { After = await ObserveAsync(step, "after", CancellationToken.None).ConfigureAwait(false) };
                        verification = PlanVerifier.Verify(plan, baseline, evidence);
                        if (settled.Error is not null || settled.Recovered || verification.OutsideScope)
                        {
                            error = settled.Error ?? (verification.OutsideScope ? new(ErrorReason.PlanStale, "Changes outside the approved scope were observed. Inspect eplan_plan_get before compiling again.", false) : null);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                error = ToFailure(ex);
                int stopped = Array.FindIndex(steps, s => s.Index == currentStep);
                if (stopped >= 0 && steps[stopped].Execution == ExecutionState.NotStarted)
                {
                    steps[stopped] = steps[stopped] with { Error = error };
                }
            }

            PlanResult result = PlanResult.Aggregate(steps, error) with { Backups = [.. backups], Verification = "skipped" };
            if (baseline is not null)
            {
                foreach (PlanStep unchanged in plan.Steps.Where(s => !s.Effective && evidence.All(e => e.Index != s.Index)))
                {
                    PlanEvidence last = evidence.LastOrDefault()?.After ?? baseline;
                    evidence.Add(new(unchanged.Index, ExecutionState.NotStarted, last, last, true));
                }
                try
                {
                    result = PlanVerifier.Verify(plan, baseline, evidence).ApplyTo(result);
                }
                catch (ArgumentException)
                {
                    // Incompatible evidence leaves verification skipped; the terminal state must still be written.
                }
            }
            if (error?.Reason == ErrorReason.ResourceExhausted)
            {
                result = result with { Verification = "skipped" };
                if (local && collecting is not null)
                {
                    JsonArray coverage = result.Coverage is { } existing ? JsonNode.Parse(existing.GetRawText())!.AsArray() : [];
                    coverage.Add(JsonSerializer.SerializeToNode(new PlanVerificationCoverage(currentStep, collecting,
                        [.. observedPages.GetValueOrDefault(currentStep) ?? []], 0, ["evidence.uncollected"]), PlanJson.Options));
                    result = result with { Coverage = JsonSerializer.SerializeToElement(coverage, PlanJson.Options) };
                }
            }
            result = result with { Coverage = JsonSerializer.SerializeToElement(new { entries = result.Coverage, evidence = references }, PlanJson.Options) };
            try
            {
                await audit.AppendAsync(new JsonObject
                {
                    ["kind"] = "plan_result",
                    ["planId"] = plan.PlanId,
                    ["result"] = JsonSerializer.SerializeToNode(result, PlanJson.Options)
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (AuditUnavailableException ex)
            {
                result = result with { Error = result.Execution == PlanExecution.Unknown ? result.Error : ToFailure(ex) };
            }
            PlanState terminal = record.State.Complete(result, packs.Clock.GetUtcNow());
            try
            {
                record = await store.UpdateAsync(plan.PlanId, record.State.Revision, _ => terminal, CancellationToken.None).ConfigureAwait(false)
                    ?? throw new IOException("Plan state revision changed.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result = result with { Error = result.Execution == PlanExecution.Unknown ? result.Error : ToFailure(ex) };
                record = record with { State = terminal with { Result = result } };
            }
            tasks.Complete(server, preparation.Task.TaskId, result, record.State.Revision, cancelled: error?.Reason == ErrorReason.Cancelled);
            return record;

            void RequireTime(TimeSpan limit)
            {
                if (packs.Clock.GetUtcNow() + limit > preparation.Deadline)
                {
                    throw Failure(ErrorReason.Timeout, "The plan's time budget is exhausted. Inspect eplan_plan_get and compile a new plan.");
                }
            }

            async Task<PlanEvidence> ObserveAsync(PlanStep step, string window, CancellationToken token)
            {
                if (local)
                {
                    collecting = window + ":uncollected";
                    if (!observedPages.TryGetValue(step.Index, out string[]? pages))
                    {
                        pages = PlanLocalEvidenceReader.Pages(step, baseline!.Snapshot);
                        observedPages.Add(step.Index, pages);
                    }
                    PlanEvidence observed = await new PlanLocalEvidenceReader(bridge, store).ReadAsync(plan, step, window, pages,
                        evidence.LastOrDefault(e => e.Index == step.Index)?.Before, references, token).ConfigureAwait(false);
                    collecting = null;
                    return observed;
                }
                // Observe all known pages as well as the scopes, so related-page alerts can stop the next write.
                var observation = new PlanEvidence(await new PlanBaselineReader(bridge).SnapshotAsync(plan.Project.Path, null, token).ConfigureAwait(false), true);
                references[step.Index.ToString(CultureInfo.InvariantCulture) + ":" + window] = await store.PutEvidenceAsync(plan.PlanId,
                    JsonSerializer.SerializeToUtf8Bytes(observation, PlanJson.Options), CancellationToken.None).ConfigureAwait(false);
                return observation;
            }
        }
    }

    private static PlanFailure ToFailure(Exception exception) => exception switch
    {
        BridgeCallException call => new(call.Error.Reason, call.Error.Message, call.Error.Retryable,
            call.Error.Metadata is null ? null : JsonSerializer.SerializeToElement(call.Error.Metadata, HostJson.Options)),
        AuditUnavailableException => new(ErrorReason.AuditUnavailable, WritePipeline.AuditUnavailableMessage, false),
        OperationCanceledException => new(ErrorReason.Cancelled, BridgeErrors.Cancelled().Message, false),
        PlanCapacityException capacity => new(capacity.Reason, capacity.Message, false),
        _ => new(ErrorReason.Internal, "The plan stopped; known commits remain committed. Inspect eplan_plan_get before compiling again.", false),
    };
}
