using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Plans;

/// <summary>Rechecks the compiler's stored baseline, using its version's content and name scope.</summary>
internal sealed class PlanApplyChecks(IBridgeClient bridge, PackLibrary packs, PlanStore store, PlanTrust trust)
{
    public Task<(EplanInstanceInfo Instance, string[] Fingerprints, string Pipe)> CheckAsync(PlanRecord record, CancellationToken cancellationToken) =>
        CheckAsync(record, false, cancellationToken);

    internal async Task<(EplanInstanceInfo Instance, string[] Fingerprints, string Pipe)> CheckAsync(PlanRecord record, bool stepOnly, CancellationToken cancellationToken)
    {
        PlanDefinition definition = record.Definition;
        bool local = UsesLocalBaseline(definition.CompilerVersion);
        if (!trust.Matches(definition, definition.Owner, record.PlanHash) || packs.Clock.GetUtcNow() >= definition.ExpiresAt)
        {
            throw Stale();
        }

        bool compiled = Directory.EnumerateFiles(packs.Locations.Audit, AuditLog.FilePattern).SelectMany(f => AuditLog.ReadLines(f))
            .Select(l => JsonNode.Parse(l)).Any(r => r?["kind"]?.GetValue<string>() == "plan_compiled"
                && r["planId"]?.GetValue<string>() == definition.PlanId && r["planHash"]?.GetValue<string>() == record.PlanHash
                && r["owner"]?.ToJsonString() == JsonSerializer.Serialize(definition.Owner, PlanJson.Options));
        if (!compiled || await store.ReadChangesAsync(definition.PlanId, cancellationToken).ConfigureAwait(false) != PlanDiffPreview.For(definition.Steps, local))
        {
            throw Stale();
        }

        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        HandshakeResult handshake = status.Handshake
            ?? throw new BridgeCallException(BridgeErrors.NotConnected());
        if (handshake.Eplan.Version != definition.EplanVersion || handshake.Eplan.Variant != definition.EplanVariant
            || definition.RequiredOperations.Except(handshake.Operations ?? [], StringComparer.Ordinal).Any())
        {
            throw Stale();
        }

        ProjectBinding? binding = await packs.Workspaces.ReadAsync(definition.Project.Path, cancellationToken).ConfigureAwait(false);
        binding = binding is { BindingVersion: 1, Cabinets.Count: 0 } ? null : binding;
        if (binding?.Revision != definition.BindingRevision || ContextBuilder.Reference(null, binding)["lockDigest"]?.GetValue<string>() != definition.LockDigest
            || binding is not null && (PlanSnapshotHashes.ProjectKey(binding.Project) != definition.Project.Path
                || binding.Cabinets.Count > 1 || binding.Cabinets.Any(c => c.Id != "default")))
        {
            throw Stale();
        }

        foreach (PlanResource resource in definition.Resources)
        {
            string folder = packs.Installed.FolderOf(resource.Pack, resource.ExactVersion);
            var (manifest, digest, problems) = await PackVerifier.ReadManifestAsync(folder, cancellationToken).ConfigureAwait(false);
            PackResource? file = manifest?.Resources.SingleOrDefault(r => r.Path == resource.ResourcePath);
            if (problems.Count > 0 || digest != resource.ManifestDigest || manifest?.Id != resource.Pack || manifest.Version != resource.ExactVersion
                || file?.Sha256 != resource.FileHash || await PackVerifier.ResourceProblemAsync(folder, file, packs.Config.PackSources, cancellationToken).ConfigureAwait(false) is not null)
            {
                throw Stale();
            }
        }

        foreach (PlanSource macro in definition.Sources.Where(s => s.Id.StartsWith("macro-", StringComparison.Ordinal)))
        {
            PlanMacroInspectData data = await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect,
                new() { Macro = macro.ResourcePath }, cancellationToken).ConfigureAwait(false);
            if (data.Sha256 != macro.FileHash)
            {
                throw Stale();
            }
        }

        if (stepOnly)
        {
            return (handshake.Eplan, [], status.Instances.FirstOrDefault(e => e.ProcessId == handshake.Eplan.ProcessId)?.PipeName ?? throw Stale());
        }

        PlanBaselineRead read = local
            ? await ReadLocalAsync(definition, cancellationToken).ConfigureAwait(false)
            : await new PlanBaselineReader(bridge).ReadAsync(definition.Project, null, cancellationToken).ConfigureAwait(false);
        if (local && read.Baseline is null && !read.Conflicts.IsEmpty)
        {
            BridgeCallException stale = Stale();
            stale.Error.Metadata!["conflicts"] = JsonSerializer.Serialize(read.Conflicts, HostJson.Options);
            throw stale;
        }
        if (read.Baseline is not { } baseline || baseline.Snapshot.ComposedHash != definition.Digests.Pages
            || baseline.Snapshot.MembersHash != definition.Digests.Objects || baseline.NamesHash != definition.Digests.Names)
        {
            throw Stale();
        }

        var fingerprints = new List<string>();
        foreach (PlanStep step in definition.Steps.Where(s => s.Prediction is null))
        {
            WritePlan fresh = await new PlanStepCompiler(bridge).PlanAsync(definition.Project.Path,
                new(step.Index, step.Op, step.Args, step.Tier, step.DependsOn), cancellationToken).ConfigureAwait(false);
            PlanChange[] changes = [.. fresh.Changes.Select(c => new PlanChange(c.Action, c.Kind, c.Name, c.Property, c.OldValue, c.NewValue))];
            if (fresh.Project != definition.Project.Name || PlanSnapshotHashes.ProjectKey(fresh.ProjectPath) != definition.Project.Path
                || !changes.SequenceEqual(step.PlannedChanges) || step.Details.Any(d => fresh.Details?.GetValueOrDefault(d.Key) != d.Value))
            {
                throw Stale();
            }

            fingerprints.Add(fresh.Fingerprint);
        }

        return (handshake.Eplan, [.. fingerprints], status.Instances.FirstOrDefault(e => e.ProcessId == handshake.Eplan.ProcessId)?.PipeName ?? throw Stale());
    }

    internal static bool UsesLocalBaseline(string compilerVersion) => compilerVersion switch
    {
        "1" => false,
        "2" => true,
        _ => throw Stale(),
    };

    private async Task<PlanBaselineRead> ReadLocalAsync(PlanDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.ContextRef is not { } reference || !reference.StartsWith("sha256:", StringComparison.Ordinal))
        {
            throw Stale();
        }
        try
        {
            byte[] bytes = await store.ReadEvidenceAsync(definition.PlanId, reference["sha256:".Length..], cancellationToken).ConfigureAwait(false);
            PlanBaseline expected = PlanJson.Read<PlanBaseline>(System.Text.Encoding.UTF8.GetString(bytes));
            if (expected.Snapshot.Project != definition.Project || expected.Snapshot.ComposedHash != definition.Digests.Pages
                || expected.Snapshot.MembersHash != definition.Digests.Objects || expected.NamesHash != definition.Digests.Names)
            {
                throw Stale();
            }
            return await new PlanLocalBaselineReader(bridge).RecheckAsync(expected, definition.Steps, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw Stale();
        }
        catch (BridgeCallException ex)
        {
            ex.Error.Execution = ExecutionState.NotStarted;
            throw;
        }
    }

    internal static bool Matches(PlanDefinition definition, PlanStep step, WritePlan fresh) =>
        fresh.MasterData is null && fresh.Project == definition.Project.Name && Path.IsPathFullyQualified(fresh.ProjectPath)
        && PlanSnapshotHashes.ProjectKey(fresh.ProjectPath) == definition.Project.Path
        && fresh.Changes.Select(c => new PlanChange(c.Action, c.Kind, c.Name, c.Property, c.OldValue, c.NewValue)).SequenceEqual(step.PlannedChanges)
        && step.Details.All(d => fresh.Details?.GetValueOrDefault(d.Key) == d.Value);

    internal static BridgeCallException Stale() => new(new BridgeError
    {
        Reason = ErrorReason.PlanStale,
        Message = "The plan's identity, lifetime or approved evidence changed. Compile a new plan; inspect eplan_plan_get first.",
        Metadata = new()
        {
            ["conflicts"] = """[{"step":null,"scope":"plan","reason":"identity_lifetime_or_evidence_changed","expected":"compiled","actual":"changed"}]""",
        },
        Execution = ExecutionState.NotStarted,
    });
}
