using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Plans;

/// <summary>Compiles validated steps through real planning or the two closed dependency predictors.</summary>
internal sealed class PlanStepCompiler(IBridgeClient bridge)
{
    public Task<PlanStep> CompileAsync(PlanBaseline baseline, PlanArgumentStep input, IReadOnlyList<PlanStep> previous,
        CancellationToken cancellationToken) => CompileAsync(baseline, input, previous, null, cancellationToken);

    public async Task<PlanStep> CompileAsync(PlanBaseline baseline, PlanArgumentStep input, IReadOnlyList<PlanStep> previous,
        PlanLocalImpact? local, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (previous.Any(s => s.Index >= input.Index) || input.DependsOn.Any(i => !previous.Any(s => s.Index == i)))
        {
            throw PlanDependencyPredictor.Refused(input, "Dependencies must reference earlier compiled steps.");
        }
        string[] targets = PlanDependencyPredictor.Targets(input);
        if (local is not null)
        {
            baseline = baseline with
            {
                Names = [.. baseline.Names, .. previous.Where(s => s.Effective)
                    .SelectMany(s => s.Preconditions.OfType<PlanAbsent>()).Select(p => new PlanOccupiedName(p.Kind, p.Name))],
            };
        }
        PlanStep[] creators = [.. previous.Where(s => s.Effective && s.Op == "page.create"
            && s.ModifyScope.Pages.Intersect(targets, StringComparer.Ordinal).Any())];
        PlanStep step;
        if (creators.Length == 0)
        {
            step = await CompileIndependentAsync(baseline, input, local, cancellationToken).ConfigureAwait(false);
        }
        else if (creators.Length == 1)
        {
            try
            {
                step = await new PlanDependencyPredictor(bridge).PredictAsync(baseline, input, creators[0], local is not null, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                throw PlanDependencyPredictor.Refused(input, "A predicted name is occupied; choose a unique name and compile again.");
            }
        }
        else
        {
            throw PlanDependencyPredictor.Refused(input, "Read/modify overlap has no predictor spanning multiple page.create steps.");
        }
        if (local is null && input.Op is ("page.create" or "macro.insert" or "device.update" or "page.renumber" or "device.renumber"))
        {
            // The collision domain also includes pages introduced earlier in this plan, outside the original snapshot.
            step = step with
            {
                ReadScope = PlanScopeRules.Pages(step.ReadScope.Pages.Concat(previous.Where(s => s.Op == "page.create").SelectMany(s => s.ModifyScope.Pages))),
            };
        }
        foreach (PlanStep earlier in previous.Where(s => s.Effective))
        {
            if (earlier.Op is "page.renumber" or "device.renumber"
                || ((PlanDependencies.Overlap(earlier.ModifyScope, step.ReadScope) || PlanDependencies.Overlap(earlier.ModifyScope, step.ModifyScope))
                    && !(step.Prediction is not null && earlier.Op == "page.create" && creators.Contains(earlier))))
            {
                throw PlanDependencyPredictor.Refused(input, "Read/modify overlap has no predictor for " + earlier.Op + " -> " + input.Op + ".");
            }
        }
        return step;
    }

    public Task<PlanStep> CompileAsync(PlanBaseline baseline, PlanArgumentStep input, CancellationToken cancellationToken) =>
        CompileAsync(baseline, input, [], cancellationToken);

    private async Task<PlanStep> CompileIndependentAsync(PlanBaseline baseline, PlanArgumentStep input, PlanLocalImpact? local, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WritePlan plan = await PlanAsync(baseline.Snapshot.Project.Path, input, cancellationToken).ConfigureAwait(false);
        if (plan.Project != baseline.Snapshot.Project.Name || !Path.IsPathFullyQualified(plan.ProjectPath)
            || PlanSnapshotHashes.ProjectKey(plan.ProjectPath) != PlanSnapshotHashes.ProjectKey(baseline.Snapshot.Project.Path))
        {
            throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PlanStale, Message = "The planning project differs from the baseline; compile a new plan." });
        }

        string[] pages = Pages(baseline, input, plan);
        string? device = input.Op == "device.update" ? plan.Changes.Single().Name : null;
        if (local is not null && input.Op is not ("page.update" or "page.delete"))
        {
            PlanMacroInspectData? macro = input.Op == "macro.insert"
                ? await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect,
                    new()
                    {
                        Macro = input.Args.GetProperty("macro").GetString(),
                    }, cancellationToken).ConfigureAwait(false) : null;
            try
            {
                return PlanStepSemantics.Build(baseline, input, PlanLocalScopeRules.Build(baseline, local, plan, macro), plan);
            }
            catch (ArgumentException)
            {
                throw PlanDependencyPredictor.Refused(input, "A requested name is already occupied; choose a unique name and compile again.");
            }
        }
        PlanImpactRead read = await new PlanImpactReader(bridge).ReadAsync(baseline,
            new(input.Op, PlanScopeRules.Pages(pages), device), cancellationToken).ConfigureAwait(false);
        if (read.Impact is not { } impact)
        {
            throw new BridgeCallException(new BridgeError
            {
                Reason = read.Reason!,
                Message = "The complete step impact cannot be compiled; check the missing evidence and scope limits.",
                Metadata = new() { ["missing"] = JsonSerializer.Serialize(read.Missing), ["violations"] = JsonSerializer.Serialize(read.Violations) },
            });
        }
        return PlanStepSemantics.Build(baseline, input, impact, plan);
    }

    internal Task<WritePlan> PlanAsync(string project, PlanArgumentStep input, CancellationToken cancellationToken)
    {
        JsonElement a = input.Args;
        string S(string name) => a.GetProperty(name).GetString()!;
        int N(string name) => a.GetProperty(name).GetInt32();
        bool B(string name) => a.GetProperty(name).GetBoolean();
        List<string> Pages() => [.. a.GetProperty("pages").EnumerateArray().Select(p => p.GetString()!)];
        Task<WritePlan> Call<T>(string op, T args) => bridge.InvokeAsync<T, WritePlan>(op, args, cancellationToken);
        return input.Op switch
        {
            "page.create" => Call(OperationNames.PageCreatePlan, new PageCreateArgs
            {
                Project = project,
                Language = ToolArguments.Optional(S("language")),
                Pages = [.. a.GetProperty("pages").EnumerateArray().Select(p => new PageCreateItem
                { Name = p.GetProperty("name").GetString()!, PageType = ToolArguments.Optional(p.GetProperty("page_type").GetString()), Description = ToolArguments.Optional(p.GetProperty("description").GetString()) })],
            }),
            "page.update" => Call(OperationNames.PageUpdatePlan, new PageUpdateArgs
            {
                Project = project,
                Language = ToolArguments.Optional(S("language")),
                Pages = [.. a.GetProperty("pages").EnumerateArray().Select(p => new PageUpdateItem
                { Name = p.GetProperty("name").GetString()!, Description = p.GetProperty("description").GetString()! })],
            }),
            "device.update" => Call(OperationNames.DeviceUpdatePlan, new DeviceUpdateArgs { Project = project, Tag = S("tag"), NewTag = S("new_tag") }),
            "macro.insert" => Call(OperationNames.MacroInsertPlan, new MacroInsertArgs
            { Project = project, Page = S("page"), Macro = S("macro"), X = a.GetProperty("x").GetDouble(), Y = a.GetProperty("y").GetDouble(), Variant = N("variant"), Numbering = S("numbering") }),
            "page.delete" => Call(OperationNames.PageDeletePlan, new PageDeleteArgs { Project = project, Pages = Pages() }),
            "page.renumber" => Call(OperationNames.PageRenumberPlan, new PageRenumberArgs
            { Project = project, Pages = Pages(), Start = N("start"), Step = N("step"), KeepText = B("keep_text"), Subpages = S("subpages") }),
            "device.renumber" => Call(OperationNames.DeviceRenumberPlan, new DeviceRenumberArgs
            { Project = project, Pages = Pages(), OnlyUnnumbered = B("only_unnumbered"), Scheme = ToolArguments.Optional(S("scheme")), Start = N("start"), Step = N("step") }),
            _ => throw new ArgumentException("Operation is not in the plan step catalog.", nameof(input)),
        };
    }

    private static string[] Pages(PlanBaseline baseline, PlanArgumentStep input, WritePlan plan)
    {
        if (input.Op == "device.update")
        {
            return [];
        }
        IEnumerable<string> names = input.Op == "macro.insert" ? [plan.Changes.Single().Name]
            : input.Args.GetProperty("pages").EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String ? p.GetString()! : p.GetProperty("name").GetString()!);
        return [.. names.Select(n => input.Op == "page.create" ? n : baseline.Snapshot.Pages.FirstOrDefault(p => p.Name == n)?.Name
            ?? baseline.Snapshot.Pages.SingleOrDefault(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))?.Name ?? n)];
    }
}
