using System.Diagnostics;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Large.Tests;

/// <summary>Read-only sample oracle: full standard snapshot plus auxiliary device listing and complete function reads.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlanNamesTests(LiveEplan eplan)
{
    [Fact]
    public async Task PlanNames_Sample_ExactlyMatchesFullBaselineNamesAndDigest()
    {
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        PlanBaseline baseline = await sample.BaselineAsync(bridge);
        var expected = baseline.Names.Select(n => (n.Kind, n.Name)).ToHashSet();
        expected.UnionWith(sample.Functions.Where(f => f.Name.Length > 0).Select(f => ("function", f.Name)));
        Assert.Equal(baseline.Names.Select(n => (n.Kind, n.Name)),
            expected.OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Name, StringComparer.Ordinal));

        var elapsed = Stopwatch.StartNew();
        PlanNamesData data = await new PlanNamesReader(bridge).ReadAsync(eplan.ProjectPath, TestContext.Current.CancellationToken);
        elapsed.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"plan.names: {elapsed.Elapsed.TotalMilliseconds:F1} ms; pages={data.Names.Count(n => n.Kind == "page")}; function names={data.Names.Count(n => n.Kind == "function")}; names={data.Names.Count}; baseline pages={sample.Snapshot.Pages.Count}; named functions={sample.Functions.Count}; page-less functions={sample.Functions.Count(f => f.Page is null)}; NamesHash={data.NamesHash}"));

        Assert.True(data.Complete);
        Assert.Equal(sample.Project.Name, data.Project);
        Assert.Equal(sample.Project.Path, data.ProjectPath);
        Assert.Equal(sample.Project.Guid, data.ProjectGuid);
        Assert.Equal(baseline.Names.Select(n => (n.Kind, n.Name)), data.Names.Select(n => (n.Kind, n.Name)));
        Assert.Equal(baseline.NamesHash, data.NamesHash);
        Assert.Equal(files, LiveSnapshots.DesignFiles(eplan.ProjectPath));
    }
}
