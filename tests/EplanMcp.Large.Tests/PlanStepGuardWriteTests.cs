using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlanStepGuardWriteTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task Guard_LargeWriteDescriptionChangedAfterPlan_ReportsExactlyOneScopeConflictAndRestores()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        PlanStep step = await PlanStepGuardTests.StepAsync(bridge, host.Writable);
        // One complete members condition proves the whole scope, so one real change has exactly one failed check.
        step = step with { Preconditions = [.. step.Preconditions.OfType<PlanMembers>()] };
        var guard = new PlanStepGuard(bridge);
        Assert.Equal("ok", await guard.CheckAsync(host.Writable, step, [], null, TestContext.Current.CancellationToken));
        JsonElement page = (await host.CallAsync("eplan_page_get", new()
        {
            ["project"] = host.Writable,
            ["page"] = PlanStepGuardTests.Page,
            ["language"] = "zh_CN",
        })).GetProperty("page");
        string? original = page.GetProperty("description").GetString();
        try
        {
            await UpdateAsync("Guard " + Guid.NewGuid().ToString("N"));
            BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() =>
                guard.CheckAsync(host.Writable, step, [], null, TestContext.Current.CancellationToken));
            Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (failure.Error.Reason, failure.Error.Execution));
            using JsonDocument json = JsonDocument.Parse(failure.Error.Metadata!["conflicts"]);
            JsonElement conflict = Assert.Single(json.RootElement.EnumerateArray());
            Assert.Equal("members", conflict.GetProperty("reason").GetString());
            Assert.Equal([PlanStepGuardTests.Page], conflict.GetProperty("scope").GetProperty("pages").EnumerateArray().Select(p => p.GetString()));
            LiveSnapshots.Snapshot actual = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable, Pages = [PlanStepGuardTests.Page] });
            string actualHash = PlanSnapshotHashes.Members(host.Writable, actual.Pages.SelectMany(p => p.Objects)
                .Select(o => new KeyValuePair<string, string>(PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o.Hash)));
            Assert.Equal(Assert.IsType<PlanMembers>(step.Preconditions[0]).Hash, conflict.GetProperty("expected").GetProperty("hash").GetString());
            Assert.Equal(actualHash, conflict.GetProperty("actual").GetProperty("hash").GetString());
            Assert.NotEqual(conflict.GetProperty("expected").GetProperty("hash").GetString(), actualHash);
            Assert.Equal(conflict.GetProperty("expected").GetProperty("count").GetInt32(), conflict.GetProperty("actual").GetProperty("count").GetInt32());
        }
        finally
        {
            await UpdateAsync(original);
        }

        Assert.Equal("ok", await guard.CheckAsync(host.Writable, step, [], null, TestContext.Current.CancellationToken));
    }

    private Task<JsonElement> UpdateAsync(string? description) => host.CallAsync("eplan_page_update", new()
    {
        ["project"] = host.Writable,
        ["pages"] = JsonSerializer.SerializeToElement(new[] { new { name = PlanStepGuardTests.Page, description } }),
        ["language"] = "zh_CN",
    });
}
