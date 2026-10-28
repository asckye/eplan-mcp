using System.Collections.Immutable;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanApplyChecksTests
{
    [Theory]
    [InlineData("1", false)]
    [InlineData("2", true)]
    [InlineData("3", true)]
    public void CompilerVersion_DispatchesApprovedBaselineSemantics(string version, bool local)
    {
        Assert.Equal(local, PlanApplyChecks.UsesLocalBaseline(version));
    }

    [Theory]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("02")]
    public void UnknownCompilerVersion_IsStaleNotStarted(string version)
    {
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => PlanApplyChecks.UsesLocalBaseline(version));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
    }

    [Theory]
    [InlineData("page.renumber", 51, 0)]
    [InlineData("device.renumber", 51, 0)]
    [InlineData("page.renumber", 1, 10001)]
    [InlineData("device.renumber", 1, 10001)]
    public async Task Renumber_KnownCoverageExceedsLimits_RefusesBeforeAnyContentRead(string op, int pages, int objects)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline([], ["+A/1"]);
        if (objects > 0)
        {
            baseline = baseline with
            {
                Snapshot = baseline.Snapshot with
                {
                    Members = Enumerable.Range(1, objects).ToImmutableDictionary(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "hash"),
                },
            };
        }
        var bridge = new FakeBridgeClient
        {
            Status = new([], new() { Operations = [OperationNames.PlanNames] }, null),
        };
        PlanOccupiedName[] names = [.. Enumerable.Range(1, pages).Select(i => new PlanOccupiedName("page", "+A/" + i))];
        bridge.Handlers[OperationNames.PlanNames] = _ => new PlanNamesData
        {
            Project = baseline.Snapshot.Project.Name,
            ProjectPath = baseline.Snapshot.Project.Path,
            ProjectGuid = baseline.Snapshot.Project.Guid,
            Complete = true,
            Names = [.. names.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })],
            NamesHash = PlanSnapshotHashes.Names(baseline.Snapshot.Project.Path, names),
        };
        var scope = PlanScopeRules.Pages(["+A/1"]);
        string[] targets = ["+A/1"];
        var step = new PlanStep(1, op, JsonSerializer.SerializeToElement(new { pages = targets }), ToolTier.Destructive,
            scope, scope, [], [], [], [], null, true);

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanLocalBaselineReader(bridge)
            .RecheckAsync(baseline, [step], TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal("plan.renumber:whole_project_guard", error.Error.Metadata!["missing"]);
        Assert.Equal(pages.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Error.Metadata["pages"]);
        Assert.Contains("eplan_page_renumber", error.Error.Message, StringComparison.Ordinal);
        Assert.Contains("eplan_device_renumber", error.Error.Message, StringComparison.Ordinal);
        Assert.Equal(OperationNames.PlanNames, Assert.Single(bridge.Calls).Operation);
    }

    [Fact]
    public async Task DeviceUpdate_StoredPageLessLocator_StillRequiresDeferredCapability()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        var bridge = new FakeBridgeClient
        {
            Status = new([], new() { Operations = [OperationNames.PlanNames] }, null),
        };
        bridge.Handlers[OperationNames.PlanNames] = _ => new PlanNamesData
        {
            Project = baseline.Snapshot.Project.Name,
            ProjectPath = baseline.Snapshot.Project.Path,
            ProjectGuid = baseline.Snapshot.Project.Guid,
            Complete = true,
            Names = [.. baseline.Names.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })],
            NamesHash = baseline.NamesHash,
        };
        var scope = new PlanScope([], [], [new(baseline.Snapshot.Project.Path, "function", 17, 11, "+A1-K1", null, null, null)]);
        var step = new PlanStep(1, "device.update", PlanTestData.Json("{}"), ToolTier.Write, PlanScopeRules.Pages([]), scope, [], [], [], [], null, true);

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanLocalBaselineReader(bridge)
            .RecheckAsync(baseline, [step], TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal("object.resolve:functions_without_page", error.Error.Metadata!["missing"]);
        Assert.Equal(OperationNames.PlanNames, Assert.Single(bridge.Calls).Operation);
    }
}
