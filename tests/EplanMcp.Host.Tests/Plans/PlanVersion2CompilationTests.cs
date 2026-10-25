using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanVersion2CompilationTests
{
    [Theory]
    [InlineData("page.create", 0, 0)]
    [InlineData("device.update", 2, 2)]
    [InlineData("macro.insert", 1, 0)]
    public async Task Compile_Version2_EmitsLocalScopesOriginalDigestsAndClosedPreconditions(string op, int pages, int locators)
    {
        PlanBaseline source = PlanScopeTestData.Baseline(op == "macro.insert" ? [] : null);
        PlanArgumentStep input = PlanStepCompilationData.Input(op);
        PlanLocalImpact impact = PlanLocalScopeRules.Resolve(source.Snapshot.Project,
            new(op, PlanScopeRules.Pages(PlanDependencyPredictor.Targets(input)), "+A1-K1"),
            source.Snapshot.Pages.Select(p => p.Name), op == "device.update"
                ? PlanScopeTestData.Functions(source).Where(f => f.Name == "+A1-K1") : []);
        PlanSnapshot snapshot = PlanVerificationTestData.Copy(source.Snapshot,
            all => all.RemoveAll(p => !impact.ReadScope.Pages.Contains(p.Name, StringComparer.Ordinal)), whole: false);
        PlanBaseline baseline = source with
        {
            Snapshot = snapshot
        };
        var bridge = new FakeBridgeClient();
        bridge.Handlers[op + ".plan"] = _ => PlanStepCompilationData.Plan(op);
        bridge.Handlers[OperationNames.PlanMacroInspect] = _ => PlanLocalScopeRulesTests.Macro(1);

        PlanStep step = await new PlanStepCompiler(bridge).CompileAsync(baseline, input, [], impact, TestContext.Current.CancellationToken);
        PlanDefinition definition = PlanTestData.Definition() with
        {
            CompilerVersion = WorkflowCompilation.CompilerVersion,
            Project = snapshot.Project,
            Steps = [step],
            Digests = new(snapshot.ComposedHash, snapshot.MembersHash, baseline.NamesHash, null),
        };

        Assert.Equal("2", definition.CompilerVersion);
        Assert.True(PlanApplyChecks.UsesLocalBaseline(definition.CompilerVersion));
        Assert.Equal(pages, step.ReadScope.Pages.Length);
        Assert.Equal(locators, step.ModifyScope.Locators.Length);
        Assert.Null(snapshot.SnapshotHash);
        Assert.Equal(PlanSnapshotHashes.Compose(snapshot.Pages), definition.Digests.Pages);
        Assert.Equal(PlanSnapshotHashes.Members(snapshot.Project.Path, snapshot.Members), definition.Digests.Objects);
        Assert.Equal(PlanSnapshotHashes.Names(snapshot.Project.Path, source.Names), definition.Digests.Names);
        Assert.Equal(snapshot.Members.Count, Assert.Single(step.Preconditions.OfType<PlanMembers>()).Count);
        Assert.Equal(snapshot.MembersHash, Assert.Single(step.Preconditions.OfType<PlanMembers>()).Hash);
        Assert.Equal(locators, step.Preconditions.OfType<PlanObjectHash>().Count(p => p.Locator.Kind == "function"));
        Assert.Equal(pages, step.Preconditions.OfType<PlanPageHash>().Count());
        Assert.Single(step.Preconditions.OfType<PlanAbsent>());
        if (op == "device.update")
        {
            Assert.Empty(step.ModifyScope.Pages);
            Assert.Equal("+A1-K9", Assert.Single(step.Preconditions.OfType<PlanAbsent>()).Name);
            Assert.Contains("update device +A1-K1", PlanDiffPreview.For([step], local: true), StringComparison.Ordinal);
        }
        Assert.DoesNotContain(bridge.Calls, c => c.Operation is OperationNames.ProjectSnapshot or OperationNames.DeviceList);
        Assert.Equal(PlanCanonical.Serialize(definition), PlanCanonical.Serialize(PlanJson.Read<PlanDefinition>(JsonSerializer.Serialize(definition, PlanJson.Options))));
    }

    [Fact]
    public void StoredVersion1_RoundTrip_PreservesCanonicalBytesHashAndDispatch()
    {
        PlanDefinition stored = PlanTestData.Definition() with
        {
            CompilerVersion = "1"
        };
        string bytes = PlanCanonical.Serialize(stored);
        PlanDefinition read = PlanJson.Read<PlanDefinition>(JsonSerializer.Serialize(stored, PlanJson.Options));

        Assert.Equal(bytes, PlanCanonical.Serialize(read));
        Assert.Equal(PlanCanonical.Hash(stored), PlanCanonical.Hash(read));
        Assert.False(PlanApplyChecks.UsesLocalBaseline(read.CompilerVersion));
        Assert.Equal(1, read.SchemaVersion);
    }

    [Fact]
    public async Task CreatedMacroPage_Version2_KeepsClosedPredictionAndOnlyTargetReadScope()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        baseline = baseline with
        {
            Snapshot = PlanVerificationTestData.Copy(baseline.Snapshot, pages => pages.Clear(), whole: false),
        };
        var bridge = new FakeBridgeClient();
        bridge.Handlers[OperationNames.PageCreatePlan] = _ => PlanStepCompilationData.Plan("page.create");
        bridge.Handlers[OperationNames.PlanMacroInspect] = _ => PlanLocalScopeRulesTests.Macro(1);
        var compiler = new PlanStepCompiler(bridge);
        PlanLocalImpact createScope = PlanLocalScopeRules.Resolve(baseline.Snapshot.Project,
            new("page.create", PlanScopeRules.Pages(["+A1/3"])), baseline.Names.Where(n => n.Kind == "page").Select(n => n.Name));
        PlanStep created = await compiler.CompileAsync(baseline, PlanStepCompilationData.Input("page.create"), [], createScope, TestContext.Current.CancellationToken);
        PlanArgumentStep input = PlanStepCompilationData.Input("macro.insert", """{"page":"+A1/3","macro":"Motor.ema","x":40.5,"y":280.25}""") with
        {
            Index = 2,
            DependsOn = [1],
        };
        PlanLocalImpact scope = PlanLocalScopeRules.Resolve(baseline.Snapshot.Project,
            new(input.Op, PlanScopeRules.Pages(["+A1/3"])), baseline.Names.Where(n => n.Kind == "page").Select(n => n.Name));

        PlanStep predicted = await compiler.CompileAsync(baseline, input, [created], scope, TestContext.Current.CancellationToken);

        Assert.NotNull(predicted.Prediction);
        Assert.Equal(["+A1/3"], predicted.ReadScope.Pages);
        Assert.Equal(["+A1/3"], predicted.ModifyScope.Pages);
        Assert.Equal(0, Assert.Single(predicted.Preconditions.OfType<PlanMembers>()).Count);
        Assert.Equal("+A1-K0", Assert.Single(predicted.Preconditions.OfType<PlanAbsent>()).Name);
        Assert.Equal(new string('a', 64), Assert.Single(predicted.Preconditions.OfType<PlanResourceHash>()).Sha256);
        Assert.DoesNotContain(bridge.Calls, c => c.Operation == OperationNames.ProjectSnapshot);
    }

    [Fact]
    public async Task TwoIndependentCreates_Version2_DoesNotAddPriorNewPageToReadScope()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        var bridge = new FakeBridgeClient();
        bridge.Handlers[OperationNames.PageCreatePlan] = _ => PlanStepCompilationData.Plan("page.create");
        var compiler = new PlanStepCompiler(bridge);
        PlanArgumentStep first = PlanStepCompilationData.Input("page.create");
        PlanLocalImpact local = PlanLocalScopeRules.Resolve(baseline.Snapshot.Project,
            new(first.Op, PlanScopeRules.Pages(["+A1/3"])), baseline.Snapshot.Pages.Select(p => p.Name));
        PlanStep created = await compiler.CompileAsync(baseline, first, [], local, TestContext.Current.CancellationToken);
        PlanArgumentStep second = PlanStepCompilationData.Input("page.create", """{"pages":[{"name":"+A1/4"}]}""") with
        {
            Index = 2
        };
        local = PlanLocalScopeRules.Resolve(baseline.Snapshot.Project,
            new(second.Op, PlanScopeRules.Pages(["+A1/4"])), baseline.Snapshot.Pages.Select(p => p.Name));
        PlanStep compiled = await compiler.CompileAsync(baseline, second, [created], local, TestContext.Current.CancellationToken);
        Assert.Empty(compiled.ReadScope.Pages);
        Assert.Equal(["+A1/4"], compiled.ModifyScope.Pages);

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() =>
            compiler.CompileAsync(baseline, second, [created], local with
            {
                ModifyScope = created.ModifyScope
            }, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }
}
