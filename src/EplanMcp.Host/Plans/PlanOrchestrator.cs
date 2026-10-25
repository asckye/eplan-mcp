using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Plans;

/// <summary>Admission through initial backup only. P23 must pass a worker token, never a request's wait token.</summary>
internal sealed class PlanOrchestrator(IBridgeClient bridge, PackLibrary packs, PlanStore store, PlanTrust trust,
    TaskRegistry tasks, SuspensionStore suspensions, DanglingWrites dangling, ApprovalService approvals, AuditLog audit,
    BackupFolders folders, PlanProjectBackup backup)
{
    public async Task<PlanPreparation> PrepareAsync(McpServer server, string planId, bool dryRun,
        CallProgress? progress, CancellationToken cancellationToken)
    {
        PlanOwner owner = tasks.OwnerFor(server);
        if (!trust.Owns(planId, owner))
        {
            throw Failure(ErrorReason.ResourceNotFound, "This plan is unavailable in this Host session.");
        }

        PlanRecord record = await store.ReadAsync(PlanPaths.Id(planId), cancellationToken).ConfigureAwait(false)
            ?? throw Failure(ErrorReason.ResourceNotFound, "This plan is unavailable in this Host session.");
        DateTimeOffset deadline = packs.Clock.GetUtcNow().AddMinutes(15);
        var checks = new PlanApplyChecks(bridge, packs, store, trust);
        if (dryRun)
        {
            _ = await checks.CheckAsync(record, cancellationToken).ConfigureAwait(false);
            return new(record, null, [], null, null, null, deadline, null, null);
        }

        RequireUnused(record.State);
        TaskRecord task = tasks.Create(server, planId, record.Definition.Project.Path, record.Definition.Steps.Count(s => s.Effective));
        FileStream? held = null;
        bool admitted = false;
        PlanBackupAttempt? copy = null;
        try
        {
            await store.CheckAdmissionAsync(planId, cancellationToken).ConfigureAwait(false);
            var paths = new PlanPaths(packs.Locations.Root);
            string key = PlanStore.Hash(Encoding.UTF8.GetBytes(PlanSnapshotHashes.ProjectKey(record.Definition.Project.Path)));
            try
            {
                held = new FileStream(paths.Check(Path.Combine(paths.Root, ".project-" + key + ".lock")), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
            {
                throw new PlanCapacityException("plans.project", 1, 1);
            }

            record = await store.UpdateAsync(planId, record.State.Revision,
                s => s.Admit(Guid.NewGuid().ToString("N"), task.TaskId, owner.RunId), cancellationToken).ConfigureAwait(false)
                ?? throw Failure(ErrorReason.InvalidArgument, ReplayMessage);
            admitted = true;
            if (!tasks.TryStart(server, task.TaskId))
            {
                throw new PlanCapacityException("tasks.project", 1, 1);
            }

            (EplanInstanceInfo instance, string[] fingerprints, string pipe) = await checks.CheckAsync(record, cancellationToken).ConfigureAwait(false);
            DateTimeOffset start = SuspensionStore.StartOf(instance.ProcessId) ?? throw PlanApplyChecks.Stale();
            if (!record.Definition.Steps.Any(s => s.Effective))
            {
                return await FinishAsync(null).ConfigureAwait(false);
            }

            await dangling.SettleOnceAsync(cancellationToken).ConfigureAwait(false);
            ToolTier tier = record.Definition.Steps.Max(s => s.Tier);
            PolicyDecision policy = PolicyEngine.Decide(tier, ScratchPaths.IsInside(record.Definition.Project.Path, packs.Config.ScratchRoots), packs.Config);
            if (policy.Verdict == PolicyVerdict.Deny)
            {
                throw Failure(ErrorReason.PolicyDenied, policy.Message!);
            }

            var view = new PlanApprovalView(record, store.ChangesPath(planId), folders.ProjectFolder(new WritePlan { ProjectPath = record.Definition.Project.Path }));
            ApprovalQuestion binding = view.Question(fingerprints, instance, deadline);
            IReadOnlyList<Suspension> blocking = suspensions.Blocking(record.Definition.Project.Path, instance.ProcessId, start);
            foreach (Suspension suspension in blocking.DistinctBy(s => s.RequestId))
            {
                tasks.Update(server, task.TaskId, new("Confirming suspended writes", 0, task.Progress.TotalSteps), waitingApproval: true);
                string releaseId = Guid.NewGuid().ToString("N");
                ApprovalResult release;
                try
                {
                    release = await approvals.ConfirmReleaseAsync(releaseId, "eplan_plan_apply", tier,
                        binding.ArgumentsHash, binding.Plan, instance, suspension, progress?.Lane(), cancellationToken).ConfigureAwait(false);
                }
                catch (BridgeCallException)
                {
                    release = new(null, ApprovalDecision.Unavailable);
                }

                await RecordDecisionAsync("suspension_release", release, suspension.RequestId, releaseId).ConfigureAwait(false);
                if (release.Decision != ApprovalDecision.Granted)
                {
                    throw Failure(ErrorReason.WritesSuspended, "Writes remain suspended. Ask the engineer to check the earlier write in EPLAN.");
                }
            }

            if (blocking.Count > 0)
            {
                suspensions.Release(record.Definition.Project.Path, instance.ProcessId, start);
            }
            if (suspensions.Blocking(record.Definition.Project.Path, instance.ProcessId, start).Count > 0)
            {
                throw Failure(ErrorReason.WritesSuspended, "Writes remain suspended; ask the engineer to check the suspension files.");
            }

            ApprovalResult? approval = null;
            if (policy.Verdict == PolicyVerdict.NeedsApproval)
            {
                tasks.Update(server, task.TaskId, new("Waiting for plan approval", 0, task.Progress.TotalSteps), waitingApproval: true);
                approval = await approvals.AskPlanAsync(store, record, view.BackupFolder, fingerprints, instance, progress?.Lane(),
                    (question, answer) => RecordDecisionAsync("plan_approval", answer, null, question.RequestId), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RecordDecisionAsync("plan_approval", null, null, binding.RequestId).ConfigureAwait(false);
            }
            if (approval is not null)
            {
                if (approval.Decision != ApprovalDecision.Granted)
                {
                    throw new BridgeCallException(approvals.Refusal(approval.Decision));
                }

                record = await ChangeAsync(s => s.Approve(new(approval.Decision, approval.Channel, binding.Plan.Fingerprint))).ConfigureAwait(false);
            }

            (EplanInstanceInfo current, string[] after, string currentPipe) = await checks.CheckAsync(record, cancellationToken).ConfigureAwait(false);
            if (current.ProcessId != instance.ProcessId || currentPipe != pipe || SuspensionStore.StartOf(current.ProcessId) != start || !fingerprints.SequenceEqual(after, StringComparer.Ordinal))
            {
                throw PlanApplyChecks.Stale();
            }

            if (packs.Clock.GetUtcNow() + PlanProjectBackup.WaitLimit > deadline)
            {
                throw Failure(ErrorReason.Timeout, "The plan's time budget is exhausted. Inspect eplan_plan_get and compile a new plan.");
            }

            record = await ChangeAsync(s => s.Start(packs.Clock.GetUtcNow(), policy.Verdict)).ConfigureAwait(false);
            tasks.Update(server, task.TaskId, new("Backing up project", 0, task.Progress.TotalSteps));
            using (BridgeProgress? reporting = BridgeProgress.Start(progress, "Backing up project", packs.Clock, cancellationToken))
            {
                copy = await backup.RunAsync(record.Definition.Project.Path, cancellationToken).ConfigureAwait(false);
            }

            if (copy.Error is not null)
            {
                throw new BridgeCallException(BackupFolders.Failure());
            }

            var ready = new PlanPreparation(record, tasks.Get(server, task.TaskId), fingerprints, instance, pipe, start, deadline, copy.Backup, held);
            held = null;
            return ready;

            async Task RecordDecisionAsync(string kind, ApprovalResult? decision, string? suspendedRequestId, string requestId)
            {
                await audit.AppendAsync(new JsonObject
                {
                    ["kind"] = kind,
                    ["requestId"] = requestId,
                    ["planId"] = planId,
                    ["planHash"] = record.PlanHash,
                    ["tier"] = tier.ToString(),
                    ["steps"] = JsonSerializer.SerializeToNode(record.Definition.Steps.Select(s => new { s.Index, s.Op, s.PlannedChanges }), PlanJson.Options),
                    ["channel"] = decision?.Channel,
                    ["decision"] = decision is null ? null : JsonNamingPolicy.SnakeCaseLower.ConvertName(decision.Decision.ToString()),
                    ["policy"] = policy.Verdict.ToString(),
                    ["fingerprint"] = binding.Plan.Fingerprint,
                    ["argumentsHash"] = binding.ArgumentsHash,
                    ["suspendedRequestId"] = suspendedRequestId,
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!admitted)
            {
                tasks.Withdraw(server, task.TaskId);
                throw;
            }

            // An admitted plan is consumed: every failure, expected or not, ends it with a result and frees its task.
            BridgeError error = ex is BridgeCallException call ? call.Error
                : ex is AuditUnavailableException ? new() { Reason = ErrorReason.AuditUnavailable, Message = WritePipeline.AuditUnavailableMessage }
                : ex is OperationCanceledException ? BridgeErrors.Cancelled()
                : ex is PlanCapacityException capacity ? new() { Reason = capacity.Reason, Message = capacity.Message }
                : ex is IOException or UnauthorizedAccessException or JsonException or PackFormatException ? PlanApplyChecks.Stale().Error
                : new() { Reason = ErrorReason.Internal, Message = "The plan stopped before its first step because of an internal error; nothing was changed. Compile a new plan." };
            return await FinishAsync(new(error.Reason, error.Message, false)).ConfigureAwait(false);
        }
        finally
        {
            held?.Dispose();
        }

        async Task<PlanRecord> ChangeAsync(Func<PlanState, PlanState> change) =>
            await store.UpdateAsync(planId, record.State.Revision, change, CancellationToken.None).ConfigureAwait(false)
                ?? throw Failure(ErrorReason.InvalidArgument, ReplayMessage);

        async Task<PlanPreparation> FinishAsync(PlanFailure? error)
        {
            PlanResult result = PlanResult.Aggregate(record.Definition.Steps.Select(s => new PlanStepResult(s.Index, s.Op, s.Effective, ExecutionState.NotStarted)), error)
                with
            { Backups = copy?.Backup is { } item ? [item] : [], Verification = error is null ? "passed" : "skipped" };
            try
            {
                await audit.AppendAsync(new JsonObject { ["kind"] = "plan_result", ["planId"] = planId, ["result"] = JsonSerializer.SerializeToNode(result, PlanJson.Options) }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (AuditUnavailableException)
            {
                result = result with { Error = new(ErrorReason.AuditUnavailable, WritePipeline.AuditUnavailableMessage, false) };
            }

            record = await ChangeAsync(s => s.Complete(result, packs.Clock.GetUtcNow())).ConfigureAwait(false);
            tasks.Complete(server, task.TaskId, result, record.State.Revision);
            return new(record, tasks.Get(server, task.TaskId), [], null, null, null, deadline, copy?.Backup, null);
        }
    }

    private const string ReplayMessage = "This plan was already admitted. Use eplan_plan_get to inspect it; compile a new plan instead of replaying it.";

    internal static void RequireUnused(PlanState state)
    {
        if (state.AttemptId is not null || state.Status != PlanStatus.Compiled)
        {
            throw Failure(ErrorReason.InvalidArgument, ReplayMessage);
        }
    }

    private static BridgeCallException Failure(string reason, string message) => new(new BridgeError { Reason = reason, Message = message, Execution = ExecutionState.NotStarted });
}
