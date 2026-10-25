using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Tools;

namespace EplanMcp.Host.Tests.Plans;

internal static class PlanPredictionTestData
{
    public const string Page = "+A1/3";

    public static PlanStep Creation(string type = "Circuit", string? description = "Power", string language = "en_US")
    {
        PlanArgumentStep input = PlanStepCompilationData.Input("page.create", JsonSerializer.Serialize(new
        {
            pages = new[] { new { name = Page, page_type = type, description } },
            language,
        }));
        PlanScope scope = PlanScopeRules.Pages([Page]);
        return PlanStepSemantics.Build(PlanScopeTestData.Baseline(), input, new(scope, scope, [], "names", []), PlanStepCompilationData.Plan("page.create"));
    }

    public static PlanArgumentStep Input(string op = "macro.insert", string? args = null) => PlanStepCompilationData.Input(op,
        args ?? (op == "page.update" ? """{"pages":[{"name":"+A1/3","description":"New"}]}"""
            : """{"page":"+A1/3","macro":"Motor.ema","x":40.5,"y":280.25}""")) with
    { Index = 2, DependsOn = [1] };

    public static PlanMacroInspectData Macro() => new()
    {
        Sha256 = new string('a', 64),
        ObjectCount = 8,
        Frame = new() { Right = 52, Bottom = -268 },
        Functions =
        [
            new() { Kind = "function", Category = "Coil", Name = "+-K?", VisibleName = "-K?", Main = true, StructureSegments = ["+"] },
            new() { Kind = "function", Category = "NoContact", Name = "+-K?", VisibleName = "-K?", StructureSegments = ["+"] },
            new() { Kind = "terminal", Category = "Terminal", Name = "+-X8:?", VisibleName = "-X8", StructureSegments = ["+"] },
            new() { Kind = "function", Category = "Wire", Name = "+", StructureSegments = ["+"] },
        ],
    };

    public static FakeBridgeClient Bridge(PlanMacroInspectData macro)
    {
        var bridge = new FakeBridgeClient();
        bridge.Handlers[OperationNames.PlanMacroInspect] = _ => macro;
        return bridge;
    }

    public static async Task<PlanStep> PredictAsync(PlanMacroInspectData? macro = null, PlanArgumentStep? input = null, PlanStep? creation = null)
        => await new PlanDependencyPredictor(Bridge(macro ?? Macro())).PredictAsync(PlanScopeTestData.Baseline([]), input ?? Input(),
            creation ?? Creation(), TestContext.Current.CancellationToken);

    public static WritePlan RealPlan(PlanStep predicted) => new()
    {
        Project = "Line1",
        ProjectPath = PlanScopeTestData.Baseline().Snapshot.Project.Path,
        Fingerprint = "fresh-session-only",
        Changes = [.. predicted.PlannedChanges.Select(c => new PlannedChange
        {
            Action = c.Action,
            Kind = c.Kind,
            Name = c.Name,
            Property = c.Property,
            OldValue = c.OldValue,
            NewValue = c.NewValue,
        })],
        Details = new() { ["macro"] = "Motor.ema", ["objectCount"] = "8" },
    };

    public static List<PlanMacroFunction> Functions(PlanMacroInspectData macro) => [.. macro.Functions.Select(f => new PlanMacroFunction
    {
        Kind = f.Kind,
        Category = f.Category,
        Main = f.Main,
        Name = PlanNamePattern.Name(Page, f),
        VisibleName = f.VisibleName,
    })];
}
