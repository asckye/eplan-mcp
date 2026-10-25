using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using static EplanMcp.Host.Tests.Plans.PlanVerificationTestData;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanVerifierSnapshotTests
{
    [Theory]
    [InlineData("page.create")]
    [InlineData("page.update")]
    [InlineData("page.delete")]
    [InlineData("page.renumber")]
    [InlineData("device.update")]
    [InlineData("device.renumber")]
    [InlineData("macro.insert")]
    public async Task PersistedStandardSnapshots_SevenOperations_VerifyVisiblePostconditions(string op)
    {
        PlanBaseline baseline = Baseline();
        PlanStep step = Compile(op);
        PlanSnapshot after = After(op, baseline.Snapshot);
        using var fixture = new PlanStoreFixture();
        Directory.CreateDirectory(fixture.Root);
        string path = Path.Combine(fixture.Root, "evidence.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new[] { baseline.Snapshot, after }, PlanJson.Options), TestContext.Current.CancellationToken);
        PlanSnapshot[] read = PlanJson.Read<PlanSnapshot[]>(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        PlanVerification report = Verify(step, read[1], before: read[0]);
        Assert.Equal(op == "macro.insert" ? "skipped" : "passed", report.Verification);
        Assert.False(report.OutsideScope);
        Assert.DoesNotContain(report.Diff, d => d.Status == "failed");
        if (op == "macro.insert")
        {
            Assert.Contains(report.Validation, v => v.Check == "macro.functions" && v.Status == "unknown");
            Assert.Contains(report.Validation, v => v.Check == "macro.graphics" && v.Status == "unknown");
        }
        else
        {
            Assert.Contains(report.Diff, d => d.Class == "planned_applied");
        }
    }

    [Fact]
    public async Task PredictedMacro_VisibleInventoryMatches_GraphicsAndPlaceholdersStillPreventWholeMacroPass()
    {
        PlanStep step = await PlanPredictionTestData.PredictAsync();
        PlanSnapshot before = After("page.create", Baseline().Snapshot);
        PlanMacroInspectData macro = PlanPredictionTestData.Macro();
        PlanSnapshot after = Copy(before, pages =>
        {
            SnapshotPage page = pages.Single(p => p.Name == PlanPredictionTestData.Page);
            long oid = 30;
            foreach (PlanMacroFunction function in PlanPredictionTestData.Functions(macro))
            {
                SnapshotObject obj = PlanScopeTestData.Function(++oid, page.Name, function.Name, function.Main);
                obj.Kind = function.Kind;
                obj.Category = function.Category;
                obj.Shown = function.VisibleName;
                page.Objects.Add(obj);
            }
        });
        PlanVerification report = Verify(step, after, before: before);
        Assert.Equal("skipped", report.Verification);
        Assert.Contains(report.Validation, v => v.Check == "macro.functions" && v.Status == "passed");
        Assert.Contains(report.Validation, v => v.Check == "macro.graphics" && v.Status == "unknown");
        Assert.Contains(report.Validation, v => v.Check == "macro.placeholders" && v.Status == "unknown");
        Assert.DoesNotContain(report.Diff, d => d.Status == "failed");

        PlanSnapshot extra = Copy(after, pages => pages.Single(p => p.Name == PlanPredictionTestData.Page).Objects.Add(
            PlanScopeTestData.Function(90, PlanPredictionTestData.Page, "+A1-K99", true)));
        PlanVerification invalid = Verify(step, extra, before: before);
        Assert.Equal("failed", invalid.Verification);
        Assert.Contains(invalid.Validation, v => v.Check == "macro.functions" && v.Status == "failed");
        Assert.Contains(invalid.Diff, d => d.Class == "unplanned_in_scope");
    }

    [Fact]
    public async Task PersistedFailedVerification_RetainsCommittedStepAndDetailedEntries()
    {
        PlanStep step = Compile("page.update");
        PlanDefinition definition = Definition(step);
        PlanResult execution = PlanResult.Aggregate([new(step.Index, step.Op, true, ExecutionState.Committed)]);
        PlanResult verified = Verify(step, Baseline().Snapshot).ApplyTo(execution);
        using var fixture = new PlanStoreFixture();
        Directory.CreateDirectory(fixture.Root);
        string path = Path.Combine(fixture.Root, "result.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(verified, PlanJson.Options), TestContext.Current.CancellationToken);
        PlanResult read = PlanJson.Read<PlanResult>(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(PlanExecution.Committed, read.Execution);
        Assert.Equal(ExecutionState.Committed, Assert.Single(read.Steps).Execution);
        Assert.Equal("failed", read.Verification);
        var record = new PlanRecord(1, PlanCanonical.Hash(definition), definition, new PlanState(Result: read));
        var entries = EplanMcp.Host.Tools.PlanEntries.Build(record, detailed: true);
        Assert.Contains(entries, e => e["section"]!.GetValue<string>() == "validation");
        Assert.Contains(entries, e => e["section"]!.GetValue<string>() == "diff");
    }
}
