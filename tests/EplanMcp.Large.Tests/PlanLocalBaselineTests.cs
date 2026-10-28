using System.Diagnostics;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Large.Tests;

/// <summary>Read-only LP-3 checks on the shared sample; oracle reads are outside the local-reader timings.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlanLocalBaselineTests(LiveEplan eplan)
{
    [Fact]
    public async Task LP3_PageCreate_ReadsNoStandardPages_AndMatchesPlanNamesHash()
    {
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(eplan.ProjectPath, TestContext.Current.CancellationToken);
        string newPage = "+LP3/" + Guid.NewGuid().ToString("N");
        var recorded = new RecordingBridge(bridge);
        var elapsed = Stopwatch.StartNew();
        PlanBaselineRead read = await new PlanLocalBaselineReader(recorded).ReadAsync(eplan.ProjectPath,
            [new("page.create", PlanScopeRules.Pages([newPage]))], (baseline, impacts) =>
            {
                PlanImpact impact = PlanLocalScopeRules.Build(baseline, Assert.Single(impacts), new()
                {
                    Project = baseline.Snapshot.Project.Name,
                    ProjectPath = baseline.Snapshot.Project.Path,
                });
                Assert.Empty(impact.ReadScope.Pages);
                Assert.Equal(0, Assert.Single(impact.Preconditions.OfType<PlanMembers>()).Count);
                Assert.Equal(newPage, Assert.Single(impact.Preconditions.OfType<PlanAbsent>()).Name);
                return Task.CompletedTask;
            }, TestContext.Current.CancellationToken);
        elapsed.Stop();
        Assert.Null(read.Reason);
        Assert.Empty(read.Conflicts);
        PlanBaseline stable = Assert.IsType<PlanBaseline>(read.Baseline);
        Assert.Empty(stable.Snapshot.Pages);
        Assert.Empty(recorded.Snapshots);
        Assert.Null(stable.Snapshot.SnapshotHash);
        Assert.Equal(names.NamesHash, stable.NamesHash);
        Assert.Equal(names.NamesHash, (await new PlanNamesReader(bridge).ReadAsync(eplan.ProjectPath, TestContext.Current.CancellationToken)).NamesHash);
        recorded.Print("page.create", elapsed.Elapsed, names.Names.Count);
        Assert.Equal(files, LiveSnapshots.DesignFiles(eplan.ProjectPath));
    }

    [Fact]
    public async Task LP3_DeviceUpdate_ReadsCompleteCrossPageClosureOnly_AndMatchesPlanNamesHash()
    {
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        var device = sample.Functions.GroupBy(f => f.Name, StringComparer.Ordinal)
            .OrderByDescending(g => g.Key == "=F+A2-KF1").ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Any(f => f.IsMain) && g.Any(f => !f.IsMain) && g.All(f => f.Page is not null)
                && g.Count() <= 200 && g.Select(f => f.Page).Distinct(StringComparer.Ordinal).Count() is > 1 and <= 50);
        Assert.SkipWhen(device is null,
            "The complete sample inventory has no fully placed same-name device with mains and auxiliaries across 2–50 pages and at most 200 functions.");
        string[] pages = [.. device!.Select(f => f.Page!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        WritePlan plan = await bridge.InvokeAsync<DeviceUpdateArgs, WritePlan>(OperationNames.DeviceUpdatePlan, new()
        {
            Project = eplan.ProjectPath,
            Tag = device.Key,
            NewTag = "-KF_LP3_" + Guid.NewGuid().ToString("N"),
        }, TestContext.Current.CancellationToken);
        PlannedChange change = Assert.Single(plan.Changes);
        PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(eplan.ProjectPath, TestContext.Current.CancellationToken);
        var recorded = new RecordingBridge(bridge);
        var elapsed = Stopwatch.StartNew();
        PlanBaselineRead read = await new PlanLocalBaselineReader(recorded).ReadAsync(eplan.ProjectPath,
            [new("device.update", PlanScopeRules.Pages([]), change.Name)], (baseline, impacts) =>
            {
                PlanImpact impact = PlanLocalScopeRules.Build(baseline, Assert.Single(impacts), plan);
                Assert.Equal(pages, impact.ReadScope.Pages);
                Assert.Empty(impact.ModifyScope.Pages);
                Assert.Empty(impact.ModifyScope.Structures);
                Assert.Equal(device.Count(), impact.ModifyScope.Locators.Length);
                Assert.Equal(device.Count(), impact.Preconditions.OfType<PlanObjectHash>().Count());
                Assert.Equal(change.NewValue, Assert.Single(impact.Preconditions.OfType<PlanAbsent>()).Name);
                return Task.CompletedTask;
            }, TestContext.Current.CancellationToken);
        elapsed.Stop();
        Assert.Null(read.Reason);
        Assert.Empty(read.Conflicts);
        PlanBaseline stable = Assert.IsType<PlanBaseline>(read.Baseline);
        Assert.Null(stable.Snapshot.SnapshotHash);
        Assert.Equal(pages, stable.Snapshot.Pages.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.NotEmpty(recorded.Snapshots);
        Assert.All(recorded.Snapshots, a => Assert.Equal(pages, a.Pages));
        Assert.All(stable.Snapshot.Pages, p => Assert.Equal(sample.Snapshot.Page(p.Name).Hash, p.Hash));
        Assert.Equal(names.NamesHash, stable.NamesHash);
        Assert.Equal(names.NamesHash, (await new PlanNamesReader(bridge).ReadAsync(eplan.ProjectPath, TestContext.Current.CancellationToken)).NamesHash);
        recorded.Print("device.update", elapsed.Elapsed, names.Names.Count);
        LivePlanSample.Describe("LP-3 complete read-only rename closure", device);
        Assert.Equal(files, LiveSnapshots.DesignFiles(eplan.ProjectPath));
    }

    private sealed class RecordingBridge(IBridgeClient inner) : IBridgeClient
    {
        private readonly Dictionary<string, double> _milliseconds = new(StringComparer.Ordinal);
        public List<ProjectSnapshotArgs> Snapshots { get; } = [];

        public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => inner.GetStatusAsync(cancellationToken);
        public Task<BridgeStatus> GetStatusAsync(bool refresh, CancellationToken cancellationToken) => inner.GetStatusAsync(refresh, cancellationToken);

        public async Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken)
        {
            Assert.Contains(operation, new[] { OperationNames.ProjectList, OperationNames.PlanNames, OperationNames.PlanFunctions, OperationNames.ProjectSnapshot });
            if (operation == OperationNames.ProjectSnapshot)
            {
                ProjectSnapshotArgs snapshot = Assert.IsType<ProjectSnapshotArgs>(args);
                Assert.NotNull(snapshot.Pages);
                Assert.NotEmpty(snapshot.Pages);
                Assert.Equal(SnapshotProfiles.StandardName, snapshot.Profile);
                Snapshots.Add(snapshot);
            }
            var elapsed = Stopwatch.StartNew();
            TData result = await inner.InvokeAsync<TArgs, TData>(operation, args, cancellationToken);
            _milliseconds[operation] = _milliseconds.GetValueOrDefault(operation) + elapsed.Elapsed.TotalMilliseconds;
            return result;
        }

        public void Print(string op, TimeSpan elapsed, int names)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"LP-3 {op}: total={elapsed.TotalMilliseconds:F1} ms, complete names={names}, standard calls={Snapshots.Count}, pages requested={string.Join(", ", Snapshots.SelectMany(a => a.Pages!).Distinct(StringComparer.Ordinal))}"));
            foreach (var timing in _milliseconds)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{timing.Key}: {timing.Value:F1} ms"));
            }
        }

        public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
