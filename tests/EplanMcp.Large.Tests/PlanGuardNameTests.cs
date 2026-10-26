using System.Diagnostics;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Large.Tests;

/// <summary>Read-only guards; discovery is outside guard timing and no execute request is sent.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlanGuardNameTests(LiveEplan eplan)
{
    [Fact]
    public async Task GuardNames_SampleExistingExtraRepresentationCaseVariantAndFreshNames()
    {
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        var finderNames = sample.Listed.Select(f => f.Tag).ToHashSet(StringComparer.Ordinal);
        string[] names = [.. sample.Functions.Select(f => f.Name).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)];
        Assert.NotEmpty(names);

        await CheckAsync(bridge, eplan.ProjectPath, "page", sample.Snapshot.Pages[0].Name, true, "existing page");
        await CheckAsync(bridge, eplan.ProjectPath, "page", "+LP4/" + Guid.NewGuid().ToString("N"), false, "fresh page");
        await CheckAsync(bridge, eplan.ProjectPath, "function", names[0], true, "existing function");
        string? extra = names.FirstOrDefault(n => !finderNames.Contains(n));
        if (extra is not null)
        {
            await CheckAsync(bridge, eplan.ProjectPath, "function", extra, true, "extra-representation-only name");
        }
        else
        {
            Output("No extra-representation-only occupied name in the complete sample oracle.");
        }

        string? variant = names.Select(n => n.ToLowerInvariant()).FirstOrDefault(n => !names.Contains(n, StringComparer.Ordinal)
            && names.Contains(n, StringComparer.OrdinalIgnoreCase));
        if (variant is not null)
        {
            await CheckAsync(bridge, eplan.ProjectPath, "function", variant, true, "case variant absent from literal inventory");
        }
        else
        {
            Output("No distinct case variant in the complete sample oracle.");
        }

        string? suffixed = names.FirstOrDefault(n => n.Contains(':', StringComparison.Ordinal));
        if (suffixed is not null)
        {
            await CheckAsync(bridge, eplan.ProjectPath, "function", suffixed[..suffixed.IndexOf(':', StringComparison.Ordinal)], true, "colon prefix");
        }
        await CheckAsync(bridge, eplan.ProjectPath, "function", FreshName(), false, "fresh function");
        Assert.Equal(files, LiveSnapshots.DesignFiles(eplan.ProjectPath));
    }

    // Needs an open S37 scale fixture (EPLANMCP_LARGE_FIXTURE); offline runs exclude it with --filter-not-trait "Fixture=Scale".
    [Fact]
    [Trait("Fixture", "Scale")]
    public async Task GuardNames_ScratchScaleFixture_EnforcesTenSecondBudgetAndPrintsPartTimings()
    {
        string? fixture = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_FIXTURE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(fixture), "Set EPLANMCP_LARGE_FIXTURE to an already-open S37 scratch project path.");
        Assert.True(Path.IsPathFullyQualified(fixture));
        Assert.True(ScratchPaths.IsInside(fixture, [LiveEplan.ScratchRoot()]));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        ProjectListData open = await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList,
            new(), TestContext.Current.CancellationToken);
        OpenProject project = Assert.Single(open.Projects, p => PlanSnapshotHashes.ProjectKey(p.Path) == PlanSnapshotHashes.ProjectKey(fixture));

        // Independent metadata discovery and page-only guard timing are reported separately from function guard timing.
        var discovery = Stopwatch.StartNew();
        PlanNamesData? names = null;
        try
        {
            names = await bridge.InvokeAsync<PlanNamesArgs, PlanNamesData>(OperationNames.PlanNames,
                new() { Project = project.Path }, TimeSpan.FromSeconds(30), null, TestContext.Current.CancellationToken);
            Assert.True(names.Complete);
        }
        catch (BridgeCallException error)
        {
            Assert.Equal(ErrorReason.Timeout, error.Error.Reason);
            Output("plan.names discovery timed out; fresh-name guard must still enforce its own budget.");
        }
        discovery.Stop();
        Output(string.Create(CultureInfo.InvariantCulture,
            $"plan.names discovery (separate call): {discovery.Elapsed.TotalMilliseconds:F1} ms; occupied names={names?.Names.Count.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}"));

        await CheckAsync(bridge, project.Path, "page", "+LP4/" + Guid.NewGuid().ToString("N"), false, "page-list-only guard");
        if (names is not null)
        {
            string existing = names.Names.First(n => n.Kind == "function").Name;
            await CheckAsync(bridge, project.Path, "function", existing, true, "existing function guard", allowBudgetTimeout: true);
        }
        await CheckAsync(bridge, project.Path, "function", FreshName(), false, "fresh function guard", allowBudgetTimeout: true);
    }

    private static async Task CheckAsync(BridgeClient bridge, string project, string kind, string name, bool occupied,
        string part, bool allowBudgetTimeout = false)
    {
        var scope = new PlanGuardScope();
        var args = new PlanStepGuardArgs
        {
            Project = project,
            ReadScope = scope,
            Preconditions =
            [
                new() { Tag = "members", Scope = scope, Count = 0, Hash = PlanSnapshotHashes.Members(project, []) },
                new() { Tag = "absent", Kind = kind, Name = name },
            ],
        };
        var clock = Stopwatch.StartNew();
        BridgeError? failure = null;
        try
        {
            string result = await bridge.InvokeAsync<PlanStepGuardArgs, string>(OperationNames.PlanStepGuard,
                args, TimeSpan.FromSeconds(30), null, TestContext.Current.CancellationToken);
            Assert.Equal("ok", result);
        }
        catch (BridgeCallException error)
        {
            failure = error.Error;
        }
        clock.Stop();
        Output(string.Create(CultureInfo.InvariantCulture,
            $"{part}: {clock.Elapsed.TotalMilliseconds:F1} ms (guard + IPC); result={failure?.Reason ?? "ok"}; kind={kind}; candidate={name}"));

        if (failure?.Reason == ErrorReason.Timeout && allowBudgetTimeout)
        {
            Assert.Equal(ExecutionState.NotStarted, failure.Execution);
            Assert.True(failure.Retryable);
            Assert.Contains("10 s", failure.Message, StringComparison.Ordinal);
            Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(10));
            // A native call cannot be interrupted; returning after 10 s is permitted only as TIMEOUT/not_started.
            return;
        }
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "A completed name guard must fit within the 10 s budget (including IPC here).");
        if (occupied)
        {
            Assert.NotNull(failure);
            Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (failure.Reason, failure.Execution));
            Assert.Contains("absent", failure.Metadata!["conflicts"], StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(failure);
        }
    }

    private static string FreshName() => "+LP4-LP4" + Guid.NewGuid().ToString("N");
    private static void Output(string message) => TestContext.Current.TestOutputHelper?.WriteLine(message);
}
