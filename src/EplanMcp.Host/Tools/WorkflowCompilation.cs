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
    public static async Task<(PlanDefinition Definition, Dictionary<string, byte[]> Evidence)> CompileAsync(IBridgeClient bridge, PackLibrary packs, PlanArgumentCheck input,
        string project, PlanOwner owner, CancellationToken cancellationToken)
    {
        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Problem is not null)
        {
            throw new BridgeCallException(status.Problem);
        }
        string[] required = [.. new[] { OperationNames.ProjectSnapshot, OperationNames.DeviceList, OperationNames.PlanFunctions, OperationNames.ProjectBackup }
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
        var reader = new PlanBaselineReader(bridge);
        PlanSnapshot identity = await reader.SnapshotAsync(project, null, cancellationToken).ConfigureAwait(false);
        ProjectBinding? binding = await BindingAsync().ConfigureAwait(false);
        string? locks = ContextBuilder.Reference(null, binding)["lockDigest"]?.GetValue<string>();
        var compiled = new List<PlanStep>();
        var sources = new List<PlanSource>();
        PlanBaselineRead read = await reader.ReadAsync(identity.Project, null, async baseline =>
        {
            foreach (PlanArgumentStep step in input.Steps)
            {
                PlanMacroInspectArgs macroArgs = new()
                {
                    Macro = step.Op == "macro.insert" ? step.Args.GetProperty("macro").GetString() : null,
                };
                PlanMacroInspectData? before = step.Op == "macro.insert"
                    ? await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect, macroArgs, cancellationToken).ConfigureAwait(false) : null;
                PlanStep result = await new PlanStepCompiler(bridge).CompileAsync(baseline, step, compiled, cancellationToken).ConfigureAwait(false);
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
        }, cancellationToken).ConfigureAwait(false);
        ProjectBinding? after = await BindingAsync().ConfigureAwait(false);
        if (read.Baseline is not { } stable || JsonSerializer.Serialize(binding) != JsonSerializer.Serialize(after))
        {
            throw new WorkflowCompilationException(ErrorReason.PlanStale, "The project or binding changed during compilation. Read again and compile a new plan.",
                new
                {
                    conflicts = read.Conflicts.IsEmpty ? [new PlanSnapshotConflict("binding", "changed", null, null)] : read.Conflicts,
                });
        }
        // Only the objects a step checks or writes are sources; the whole snapshot is the evidence that contextRef references.
        var referenced = new HashSet<(string Kind, int Type, long Oid)>(compiled.SelectMany(s => s.Preconditions.OfType<PlanObjectHash>()
            .Select(p => (p.Locator.Kind, p.Locator.Type, p.Locator.Oid))
            .Concat(s.ModifyScope.Locators.Select(l => (l.Kind, l.Type, l.Oid)))));
        sources.AddRange(stable.Snapshot.Pages.SelectMany(p => p.Objects).Where(o => referenced.Contains((o.Kind, o.Type, o.Oid))).Select(o => new PlanSource(
            "object-" + o.Kind + "-" + o.Type.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + o.Oid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            stable.Snapshot.Project.Path, o.Kind, o.Type, o.Oid, o.Hash, null, null, null, null, null)));
        DateTimeOffset now = packs.Clock.GetUtcNow();
        byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(stable, PlanJson.Options);
        string evidenceHash = PlanStore.Hash(evidence);
        var definition = new PlanDefinition(Guid.NewGuid().ToString("N"), stable.Snapshot.Project, "default", owner, now, now.AddHours(packs.Config.PlanLifetimeHours),
            1, "1", 1, status.Handshake!.Eplan.Version, status.Handshake.Eplan.Variant ?? "unknown", [.. required],
            new([.. input.Steps.Select(s => new PlanInputStep(s.Op, s.Args))]), binding?.Revision, locks, "sha256:" + evidenceHash, [],
            [.. sources.Distinct().OrderBy(s => s.Id, StringComparer.Ordinal)], new(stable.Snapshot.ComposedHash, stable.Snapshot.MembersHash, stable.NamesHash, null), [.. compiled]);
        return (definition, new()
        {
            [evidenceHash] = evidence,
        });

        async Task<ProjectBinding?> BindingAsync()
        {
            ProjectBinding? value = await packs.Workspaces.ReadAsync(identity.Project.Path, cancellationToken).ConfigureAwait(false);
            if (value is not null && (PlanSnapshotHashes.ProjectKey(value.Project) != identity.Project.Path
                || value.Cabinets.Count > 1 || value.Cabinets.Any(c => c.Id != "default")))
            {
                throw new WorkflowCompilationException(ErrorReason.CapabilityMissing, "Use a single default cabinet binding and compile again.", new
                {
                    missing = (string[])["cabinet:single_default_binding"],
                });
            }
            return value?.Cabinets.Count == 0 ? null : value;
        }
    }
}

internal sealed class WorkflowCompilationException(string reason, string message, object metadata) : Exception(message)
{
    public string Reason { get; } = reason;
    public object Metadata { get; } = metadata;
}
