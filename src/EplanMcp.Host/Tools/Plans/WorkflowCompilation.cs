using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tools;

/// <summary>Assembles immutable evidence between the baseline reader's two complete reads.</summary>
internal static class WorkflowCompilation
{
    internal const string CompilerVersion = "2";

    public static async Task<(PlanDefinition Definition, Dictionary<string, byte[]> Evidence)> CompileAsync(IBridgeClient bridge, PackLibrary packs, PlanArgumentCheck input,
        string project, PlanOwner owner, KnowledgeReviewService? reviews, string? cabinet, string? workflow, JsonElement inputs, CancellationToken cancellationToken, IReadOnlyList<RuleResult>? repairs = null)
    {
        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Problem is not null)
        {
            throw new BridgeCallException(status.Problem);
        }
        string[] required = [.. new[] { OperationNames.ProjectList, OperationNames.PlanNames, OperationNames.ProjectSnapshot, OperationNames.PlanFunctions, OperationNames.ProjectBackup }
            .Concat(input.Steps.SelectMany(s => new[] { s.Op + ".plan", s.Op + ".execute" }))
            .Concat(input.Steps.Any(s => s.Op == "macro.insert") ? [OperationNames.PlanMacroInspect] : []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] missing = [.. required.Except(status.Handshake?.Operations ?? [], StringComparer.Ordinal).Select(op => "op:" + op)];
        if (missing.Length > 0)
        {
            throw new WorkflowCompilationException(ErrorReason.CapabilityMissing, "Install a Bridge supporting the required plan operations and compile again.", new
            {
                missing,
            });
        }
        var compiler = new PlanStepCompiler(bridge);
        var requests = new List<PlanImpactRequest>();
        foreach (PlanArgumentStep step in input.Steps)
        {
            string? device = step.Op == "device.update"
                ? (await compiler.PlanAsync(project, step, cancellationToken).ConfigureAwait(false)).Changes.Single().Name : null;
            requests.Add(new(step.Op, PlanScopeRules.Pages(PlanDependencyPredictor.Targets(step)), device));
        }
        PlanProject? identity = null;
        ProjectBinding? binding = null;
        string? locks = null;
        WorkflowEngineering? engineering = null;
        PlanEngineering? provenance = null;
        var compiled = new List<PlanStep>();
        var sources = new List<PlanSource>();
        PlanBaselineRead read = await new PlanLocalBaselineReader(bridge).ReadAsync(project, requests, async (baseline, impacts) =>
        {
            identity = baseline.Snapshot.Project;
            binding = await BindingAsync().ConfigureAwait(false);
            if (workflow is not null && binding?.BindingVersion != 2)
            { throw PlanLocalScopeRules.Stale("The workflow binding changed; compile again."); }
            locks = ContextBuilder.Reference(null, binding)["lockDigest"]?.GetValue<string>();
            if (binding?.BindingVersion == 2)
            {
                engineering = await WorkflowEngineering.ReadAsync(packs, binding, cabinet, workflow, inputs, cancellationToken).ConfigureAwait(false);
                string[] capabilities = [.. engineering.Workflow?.Document?.Spec["requiredCapabilities"]?.AsArray().Select(c => c!.GetValue<string>().StartsWith("op:", StringComparison.Ordinal) ? c.GetValue<string>()[3..] : c.GetValue<string>()) ?? []];
                if (capabilities.Except(status.Handshake!.Operations ?? [], StringComparer.Ordinal).Any())
                { throw PlanLocalScopeRules.Missing("workflow:required_capabilities"); }
                required = [.. required.Concat(capabilities).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
                if (workflow is not null && JsonSerializer.Serialize(engineering.Expand(project).Steps) != JsonSerializer.Serialize(input.Steps))
                { throw PlanLocalScopeRules.Stale("Workflow inputs changed during expansion; compile again."); }
            }
            else
            { _ = ContextBuilder.SelectCabinet(binding, cabinet); }
            foreach (PlanArgumentStep step in input.Steps)
            {
                PlanMacroInspectArgs macroArgs = new()
                {
                    Macro = step.Op == "macro.insert" ? step.Args.GetProperty("macro").GetString() : null,
                };
                PlanMacroInspectData? before = step.Op == "macro.insert"
                    ? await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect, macroArgs, cancellationToken).ConfigureAwait(false) : null;
                PlanStep result = await compiler.CompileAsync(baseline, step, compiled, impacts[step.Index - 1], cancellationToken).ConfigureAwait(false);
                if (step.Op == "macro.insert")
                {
                    PlanMacroInspectData macro = await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect,
                        macroArgs, cancellationToken).ConfigureAwait(false);
                    if (before!.Sha256 != macro.Sha256 || result.Preconditions.OfType<PlanResourceHash>().Any(p => p.Sha256 != macro.Sha256))
                    {
                        throw new WorkflowCompilationException(ErrorReason.PlanStale, "The macro changed during compilation; compile again.", new
                        {
                            conflicts = new[] { new PlanSnapshotConflict("macro", "changed", before.Sha256, macro.Sha256) },
                        });
                    }
                    result = result with
                    {
                        Preconditions = [.. result.Preconditions, new PlanResourceHash(macro.Sha256)],
                    };
                    sources.Add(new("macro-" + PlanStore.Hash(JsonSerializer.SerializeToUtf8Bytes(macroArgs, PlanJson.Options)), null, null, null, null, null, null, null, macroArgs.Macro, null, macro.Sha256));
                }
                if (binding is not null)
                {
                    result = result with
                    {
                        Preconditions = [.. result.Preconditions, new PlanBinding(binding.Revision, locks!)],
                    };
                }
                compiled.Add(result);
            }
            if (engineering is not null)
            {
                engineering.Guard(binding!, baseline, compiled);
                provenance = await engineering.CompleteAsync(reviews!, baseline, cancellationToken, repairs: repairs).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
        ProjectBinding? after = identity is null ? null : await BindingAsync().ConfigureAwait(false);
        if (read.Baseline is not { } stable || JsonSerializer.Serialize(binding) != JsonSerializer.Serialize(after))
        {
            throw new WorkflowCompilationException(ErrorReason.PlanStale, "The project or binding changed during compilation. Read again and compile a new plan.",
                new
                {
                    conflicts = read.Conflicts.IsEmpty ? [new PlanSnapshotConflict("binding", "changed", null, null)] : read.Conflicts,
                });
        }
        if (engineering is not null)
        {
            WorkflowEngineering current = await WorkflowEngineering.ReadAsync(packs, after!, cabinet, workflow, inputs, cancellationToken).ConfigureAwait(false);
            if (!engineering.Resources.SequenceEqual(current.Resources) || engineering.Config.ToJsonString() != current.Config.ToJsonString())
            { throw PlanLocalScopeRules.Stale("Locked configuration or knowledge changed during compilation; compile again."); }
            foreach (AssetEntry rule in current.Assets.Where(a => a.Document?.Kind == EplanMcp.Packs.Knowledge.KnowledgeKind.Rule))
            {
                if (await reviews!.CheckPlanAsync(rule, current.Assets, cancellationToken).ConfigureAwait(false) != provenance!.RuleDigests.GetProperty(rule.Id).GetProperty("reviewDigest").GetString())
                { throw PlanLocalScopeRules.Stale("Knowledge confirmation changed during compilation; compile again."); }
            }
            sources.AddRange(engineering.Resources.Select(r => new PlanSource("asset-" + r.Pack + "-" + r.ResourcePath, null, null, null, null, null, r.Pack, r.ExactVersion, r.ResourcePath, r.ManifestDigest, r.FileHash)));
        }
        // Only checked or written objects are sources; contextRef references the local baseline and complete names.
        var referenced = new HashSet<(string Kind, int Type, long Oid)>(compiled.SelectMany(s => s.Preconditions.OfType<PlanObjectHash>()
            .Select(p => (p.Locator.Kind, p.Locator.Type, p.Locator.Oid))
            .Concat(s.ModifyScope.Locators.Select(l => (l.Kind, l.Type, l.Oid)))));
        sources.AddRange(stable.Snapshot.Pages.SelectMany(p => p.Objects).Where(o => referenced.Contains((o.Kind, o.Type, o.Oid))).Select(o => new PlanSource(
            "object-" + o.Kind + "-" + o.Type.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + o.Oid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            stable.Snapshot.Project.Path, o.Kind, o.Type, o.Oid, o.Hash, null, null, null, null, null)));
        DateTimeOffset now = packs.Clock.GetUtcNow();
        byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(stable, PlanJson.Options);
        string evidenceHash = PlanStore.Hash(evidence);
        var definition = new PlanDefinition(Guid.NewGuid().ToString("N"), stable.Snapshot.Project, engineering?.Cabinet.Id ?? "default", owner, now, now.AddHours(packs.Config.PlanLifetimeHours),
            engineering is null ? 1 : 2, engineering is null ? CompilerVersion : "3", 1, status.Handshake!.Eplan.Version, status.Handshake.Eplan.Variant ?? "unknown", [.. required],
            new([.. input.Steps.Select(s => new PlanInputStep(s.Op, s.Args))]), binding?.Revision, locks, "sha256:" + evidenceHash, [.. engineering?.Resources ?? []],
            [.. sources.Distinct().OrderBy(s => s.Id, StringComparer.Ordinal)], new(stable.Snapshot.ComposedHash, stable.Snapshot.MembersHash, stable.NamesHash, null), [.. compiled])
        { Engineering = provenance };
        return (definition, new()
        {
            [evidenceHash] = evidence,
        });

        async Task<ProjectBinding?> BindingAsync()
        {
            ProjectBinding? value = await packs.Workspaces.ReadAsync(identity!.Path, cancellationToken).ConfigureAwait(false);
            if (value is not null && (PlanSnapshotHashes.ProjectKey(value.Project) != identity.Path
                || value.BindingVersion == 1 && (value.Cabinets.Count > 1 || value.Cabinets.Any(c => c.Id != "default"))))
            {
                throw new WorkflowCompilationException(ErrorReason.CapabilityMissing, "Use a single default cabinet binding and compile again.", new
                {
                    missing = (string[])["cabinet:single_default_binding"],
                });
            }
            return value is { BindingVersion: 1, Cabinets.Count: 0 } ? null : value;
        }
    }
}

internal sealed class WorkflowCompilationException(string reason, string message, object metadata) : Exception(message)
{
    public string Reason { get; } = reason;
    public object Metadata { get; } = metadata;
}
