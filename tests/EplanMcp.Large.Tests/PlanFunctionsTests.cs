using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>Part A against the checked sample fixture: discovery and assertions use only read operations.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlanFunctionsTests(LiveEplan eplan)
{
    [Fact]
    public async Task PlanFunctions_Sample_UnplacedFunctionIsReturned()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        PlanFunction? unplaced = sample.Functions.FirstOrDefault(f => f.Page is null);
        Assert.SkipWhen(unplaced is null,
            "EPLAN_Sample_Project has no named page-less function: all device.list(includeAuxiliary=true) pages and all snapshot names were checked with complete plan.functions reads.");
        PlanFunctionsData data = await ReadAsync(bridge, unplaced!.Name);
        PlanFunction found = Assert.Single(data.Functions, f => f.Type == unplaced.Type && f.Oid == unplaced.Oid);
        Assert.Equal((unplaced.Name, unplaced.IsMain, (string?)null), (found.Name, found.IsMain, found.Page));
        LivePlanSample.Describe("Page-less function", [found]);
    }

    [Fact]
    public async Task PlanFunctions_Sample_SameNameMainsAcrossPagesAreAllReturned()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        var mains = sample.Snapshot.Objects("function").Where(f => f.Main == true && !string.IsNullOrEmpty(f.Name))
            .GroupBy(f => f.Name!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Select(f => f.Page).Distinct(StringComparer.Ordinal).Count() > 1);
        Assert.SkipWhen(mains is null, "EPLAN_Sample_Project has no full name with main functions on two different pages; the complete snapshot was checked.");
        PlanFunctionsData data = await ReadAsync(bridge, mains!.Key);
        Assert.Equal(mains.Select(f => (f.Type, f.Oid, f.Page)).OrderBy(f => f.Type).ThenBy(f => f.Oid),
            data.Functions.Where(f => f.IsMain && f.Page is not null).Select(f => (f.Type, f.Oid, f.Page)).OrderBy(f => f.Type).ThenBy(f => f.Oid));
        AssertGroup(sample, mains.Key, data);
        LivePlanSample.Describe("Same-name mains across pages", data.Functions.Where(f => f.IsMain));
    }

    [Fact]
    public async Task PlanFunctions_Sample_MoreThanFiftyAuxiliariesAreAllReturned()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        var auxiliaries = sample.Snapshot.Objects("function").Where(f => f.Main == false && !string.IsNullOrEmpty(f.Name)).Select(f => f.Name!)
            .Concat(sample.Listed.Where(f => !f.Main && f.Page is null).Select(f => f.Tag)).GroupBy(name => name, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 50);
        Assert.SkipWhen(auxiliaries is null,
            "EPLAN_Sample_Project has no full name with more than 50 auxiliary functions; all snapshot placements and every device.list(includeAuxiliary=true) page were checked without device.get's 50-item limit.");
        PlanFunctionsData data = await ReadAsync(bridge, auxiliaries!.Key);
        Assert.Equal(auxiliaries.Count(), data.Functions.Count(f => !f.IsMain));
        AssertGroup(sample, auxiliaries.Key, data);
        LivePlanSample.Describe("More than fifty auxiliaries", data.Functions.Where(f => !f.IsMain));
    }

    private async Task<PlanFunctionsData> ReadAsync(BridgeClient bridge, string name)
    {
        PlanFunctionsData data = await bridge.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
            new() { Project = eplan.ProjectPath, Names = [name] }, TestContext.Current.CancellationToken);
        Assert.True(data.Complete, "The selected device exceeds plan.functions' limit; it must not be treated as a complete result or skipped.");
        Assert.Equal(10_000, data.Limit);
        Assert.All(data.Functions, f => Assert.Equal(name, f.Name));
        return data;
    }

    private static void AssertGroup(LivePlanSample sample, string name, PlanFunctionsData data) =>
        Assert.Equal(sample.Functions.Where(f => f.Name == name).Select(f => (f.Type, f.Oid, f.IsMain, f.Page)).OrderBy(f => f.Type).ThenBy(f => f.Oid),
            data.Functions.Select(f => (f.Type, f.Oid, f.IsMain, f.Page)).OrderBy(f => f.Type).ThenBy(f => f.Oid));
}
