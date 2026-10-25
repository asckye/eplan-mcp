using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Large.Tests;

/// <summary>Writes only to LiveEplan's checked scratch write copy; verifies the empty-page prediction against EPLAN.</summary>
[Trait("Size", "Large")]
public sealed class PlanDependencyPredictionTests(LiveEplan eplan)
{
    [Fact]
    public async Task CreateThenKeepMacro_ApprovedPredictionMatchesRealPlanAndInsertedFunctions()
    {
        string project = await eplan.WriteProjectAsync();
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        var reader = new PlanBaselineReader(bridge);
        PlanSnapshot identity = await reader.SnapshotAsync(project, null, TestContext.Current.CancellationToken);
        PlanBaselineRead read = await reader.ReadAsync(identity.Project, null, TestContext.Current.CancellationToken);
        Assert.Null(read.Reason);
        PlanBaseline baseline = Assert.IsType<PlanBaseline>(read.Baseline);
        const string prefix = "=GAA+A1&EFS1/";
        int last = baseline.Snapshot.Pages.Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(p => int.TryParse(p.Name[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0).DefaultIfEmpty().Max();
        string page = prefix + (last + 1).ToString(CultureInfo.InvariantCulture);
        Dictionary<string, string> before = LiveSnapshots.DesignFiles(project);
        (PlanStep create, PlanStep predicted, PlanMacroInspectData inspected) = await FindPredictionAsync(bridge, baseline, page);
        Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.True(Assert.Single(predicted.PlannedChanges).Prediction);

        var createArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(create.Args.GetRawText())!;
        createArgs["project"] = project;
        Assert.Equal("committed", (await eplan.CallAsync("eplan_page_create", createArgs)).GetProperty("execution").GetString());
        var macroArgs = new MacroInsertArgs
        {
            Project = project,
            Page = page,
            Macro = predicted.Args.GetProperty("macro").GetString(),
            X = predicted.Args.GetProperty("x").GetDouble(),
            Y = predicted.Args.GetProperty("y").GetDouble(),
            Variant = 0,
            Numbering = "keep",
        };
        WritePlan real = await bridge.InvokeAsync<MacroInsertArgs, WritePlan>(OperationNames.MacroInsertPlan, macroArgs, TestContext.Current.CancellationToken);
        PlanMacroInspectData fresh = await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect,
            new() { Macro = macroArgs.Macro }, TestContext.Current.CancellationToken);
        Assert.Equal(inspected.Sha256, fresh.Sha256);
        List<PlanMacroFunction> planned = [.. fresh.Functions.Select(f => new PlanMacroFunction
        {
            Kind = f.Kind,
            Category = f.Category,
            Main = f.Main,
            Name = PlanNamePattern.Name(page, f),
            VisibleName = f.VisibleName,
        })];
        Assert.True(PlanPredictionMatch.Matches(predicted, real, predicted.Args, fresh, planned));

        var insertArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(predicted.Args.GetRawText())!;
        insertArgs["project"] = project;
        JsonElement inserted = await eplan.CallAsync("eplan_macro_insert", insertArgs);
        Assert.Equal("committed", inserted.GetProperty("execution").GetString());
        Assert.Equal(inspected.ObjectCount, inserted.GetProperty("objectCount").GetInt32());
        LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, new() { Project = project, Pages = [page] });
        Assert.True(after.Chunks[^1].Complete);
        List<SnapshotObject> actual = [.. after.Objects("function")];
        JsonElement constraint = Assert.Single(predicted.Expected).GetProperty("value");
        Assert.Equal(constraint.GetProperty("functionCount").GetInt32(), actual.Count);
        foreach (JsonElement allowed in constraint.GetProperty("functions").EnumerateArray())
        {
            int index = actual.FindIndex(f => f.Category == allowed.GetProperty("category").GetString()
                && f.Main == allowed.GetProperty("main").GetBoolean()
                && PlanNamePattern.Matches(allowed.GetProperty("namePattern").GetString()!, f.Name ?? "")
                && PlanNamePattern.Matches(allowed.GetProperty("visibleNamePattern").GetString()!, f.Shown ?? ""));
            Assert.True(index >= 0, "Inserted functions must satisfy every approved literal structure, label, category and main flag.");
            actual.RemoveAt(index);
        }
        Assert.Empty(actual);
        TestContext.Current.TestOutputHelper!.WriteLine($"Matched {inspected.Functions.Count} functions on {page}, macro {macroArgs.Macro}, SHA-256 {inspected.Sha256}.");
    }

    private async Task<(PlanStep Create, PlanStep Predicted, PlanMacroInspectData Inspection)> FindPredictionAsync(
        BridgeClient bridge, PlanBaseline baseline, string page)
    {
        int checkedCount = 0;
        string? cursor = null;
        do
        {
            JsonElement list = await eplan.CallAsync("eplan_macro_list", new() { ["limit"] = 200, ["cursor"] = cursor });
            string? next = list.GetProperty("nextCursor").GetString();
            Assert.True(!list.GetProperty("truncated").GetBoolean() || next is not null);
            foreach (JsonElement item in list.GetProperty("macros").EnumerateArray())
            {
                string macro = item.GetProperty("path").GetString()!;
                checkedCount++;
                PlanMacroInspectData inspected;
                try
                {
                    inspected = await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect,
                        new() { Macro = macro }, TestContext.Current.CancellationToken);
                }
                catch (BridgeCallException ex) when (ex.Error.Reason is ErrorReason.CapabilityMissing or ErrorReason.InvalidArgument)
                {
                    continue;
                }
                if (inspected.Functions.Count == 0 || inspected.Frame.Right > 380 || inspected.Frame.Bottom < -280)
                {
                    continue;
                }
                string request = JsonSerializer.Serialize(new
                {
                    project = baseline.Snapshot.Project.Path,
                    steps = new object[]
                    {
                        new { op = "page.create", args = new { pages = new[] { new { name = page, description = "Prediction check" } } } },
                        new { op = "macro.insert", args = new { page = new { from_step = 1, page }, macro, x = 40, y = 280, numbering = "keep" } },
                    },
                });
                PlanArgumentCheck input = PlanArguments.Check(JsonDocument.Parse(request).RootElement, "en_US", "IEC");
                Assert.Empty(input.Violations);
                var compiler = new PlanStepCompiler(bridge);
                PlanStep create = await compiler.CompileAsync(baseline, input.Steps[0], [], TestContext.Current.CancellationToken);
                try
                {
                    PlanStep predicted = await compiler.CompileAsync(baseline, input.Steps[1], [create], TestContext.Current.CancellationToken);
                    return (create, predicted, inspected);
                }
                catch (BridgeCallException ex) when (ex.Error.Reason == ErrorReason.InvalidArgument
                    && ex.Error.Message.Contains("occupied", StringComparison.Ordinal))
                {
                    // Keep cannot merge with any existing device part, even on a different structure (A14).
                }
            }
            Assert.True(next is null || next != cursor);
            cursor = next;
        }
        while (cursor is not null);

        Assert.Skip($"No supported conflict-free MultiLine variant 0 macro fitting x=40, y=280 after checking all {checkedCount} listed macros.");
        return default;
    }
}
