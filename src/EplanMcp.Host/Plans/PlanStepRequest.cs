using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Closed typed adapter; dependent guards retain original checks and bind only proven created pages.</summary>
internal static class PlanStepRequest
{
    private static readonly JsonSerializerOptions _args = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static PlanStepExecuteArgs Build(PlanDefinition plan, PlanStep step, WritePlan fresh,
        IReadOnlyList<PlanStepEvidence> evidence, PlanEvidence baseline, PlanMacroInspectData? macro)
    {
        var conditions = step.Preconditions.ToList();
        if (step.Prediction is { } prediction)
        {
            int source = prediction.GetProperty("fromStep").GetInt32();
            PlanStep creator = plan.Steps.Single(s => s.Index == source);
            PlanStepEvidence observed = evidence.Single(e => e.Index == source);
            PlanVerification proof = PlanVerifier.Verify(plan with { Steps = [creator] }, baseline, [observed]);
            if (observed.Execution != ExecutionState.Committed || observed.After is null || proof.Verification != "passed")
            {
                throw PlanApplyChecks.Stale();
            }

            conditions.AddRange(PlanPreconditions.Build(new(observed.After.Snapshot, [], ""), step.ReadScope, detail: step.ModifyScope));
        }

        JsonObject input = JsonNode.Parse(step.Args.GetRawText())!.AsObject();
        input["project"] = plan.Project.Path;
        T Read<T>() => input.Deserialize<T>(_args)!;
        var result = new PlanStepExecuteArgs
        {
            PlanId = plan.PlanId,
            Index = step.Index,
            Total = plan.Steps.Length,
            Fingerprint = fresh.Fingerprint,
            ApprovedChanges = fresh.Changes,
            ApprovedDetails = fresh.Details,
            Guard = new()
            {
                Project = plan.Project.Path,
                ReadScope = JsonSerializer.Deserialize<PlanGuardScope>(JsonSerializer.Serialize(step.ReadScope, PlanJson.Options), PlanJson.Options)!,
                Preconditions = JsonSerializer.Deserialize<List<PlanGuardCondition>>(JsonSerializer.Serialize(conditions, PlanJson.Options), PlanJson.Options)!,
                ResourceHashes = [.. plan.Resources.Select(r => r.FileHash).Concat(plan.Sources.Select(s => s.FileHash).OfType<string>()).Distinct(StringComparer.Ordinal)],
                BindingRevision = plan.BindingRevision,
                LockDigest = plan.LockDigest,
            },
        };
        switch (step.Op)
        {
            case "page.create":
                result.PageCreate = Read<PageCreateArgs>();
                break;
            case "page.update":
                result.PageUpdate = Read<PageUpdateArgs>();
                break;
            case "page.delete":
                result.PageDelete = Read<PageDeleteArgs>();
                break;
            case "page.renumber":
                result.PageRenumber = Read<PageRenumberArgs>();
                break;
            case "device.update":
                result.DeviceUpdate = Read<DeviceUpdateArgs>();
                break;
            case "device.renumber":
                result.DeviceRenumber = Read<DeviceRenumberArgs>();
                break;
            case "macro.insert":
                result.MacroInsert = Read<MacroInsertArgs>();
                result.MacroConstraint = new() { Arguments = result.MacroInsert, Description = macro! };
                break;
            default:
                throw new InvalidOperationException("Unsupported compiled step.");
        }

        return result;
    }
}
