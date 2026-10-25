using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlanStepGuardTests(LiveEplan eplan)
{
    internal const string Page = "=GAA+A1&EFS1/1";

    [Fact]
    public async Task Guard_SampleUnchangedScope_PassesOnMainThread()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PlanStep step = await StepAsync(bridge, eplan.ProjectPath);
        Assert.Equal("ok", await new PlanStepGuard(bridge).CheckAsync(eplan.ProjectPath, step, [], null, TestContext.Current.CancellationToken));
    }

    internal static async Task<PlanStep> StepAsync(BridgeClient bridge, string project)
    {
        LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(bridge, new() { Project = project, Pages = [Page] });
        ProjectSnapshotData head = snapshot.Chunks[0];
        PlanBaselineRead read = await new PlanBaselineReader(bridge).ReadAsync(new(head.Project, head.ProjectPath, head.ProjectGuid),
            [Page], TestContext.Current.CancellationToken);
        Assert.Null(read.Reason);
        Assert.Empty(read.Conflicts);
        PlanBaseline baseline = Assert.IsType<PlanBaseline>(read.Baseline);
        PlanScope scope = PlanScopeRules.Pages([Page]);
        return new(0, "page.update", JsonSerializer.SerializeToElement(new { }), ToolTier.Write, scope, scope,
            PlanPreconditions.Build(baseline, scope), [], [], [], null, true);
    }
}

