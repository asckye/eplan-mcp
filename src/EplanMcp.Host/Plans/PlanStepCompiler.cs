using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Plans;

/// <summary>Compiles one validated independent step through the existing read-only planning operations.</summary>
internal sealed class PlanStepCompiler(IBridgeClient bridge)
{
    public async Task<PlanStep> CompileAsync(PlanBaseline baseline, PlanArgumentStep input, CancellationToken cancellationToken)
    {
        if (!input.DependsOn.IsEmpty)
        {
            throw new ArgumentException("Dependent steps require a predictor.", nameof(input));
        }
        cancellationToken.ThrowIfCancellationRequested();
        WritePlan plan = await PlanAsync(baseline.Snapshot.Project.Path, input, cancellationToken).ConfigureAwait(false);
        if (plan.Project != baseline.Snapshot.Project.Name || !Path.IsPathFullyQualified(plan.ProjectPath)
            || PlanSnapshotHashes.ProjectKey(plan.ProjectPath) != PlanSnapshotHashes.ProjectKey(baseline.Snapshot.Project.Path))
        {
            throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PlanStale, Message = "The planning project differs from the baseline; compile a new plan." });
        }

        string[] pages = Pages(baseline, input, plan);
        string? device = input.Op == "device.update" ? plan.Changes.Single().Name : null;
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

    private Task<WritePlan> PlanAsync(string project, PlanArgumentStep input, CancellationToken cancellationToken)
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
