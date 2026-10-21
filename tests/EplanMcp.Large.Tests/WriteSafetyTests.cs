using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Quality scenarios Q1, Q2 and Q6 of the write tools on EPLAN itself (M3 design 7; README.md). The Medium tests run the
/// same scenarios against a fake project (PageCreateInteropTests, WritePipelineTests); what only EPLAN can show is that
/// the policy refuses before EPLAN is touched, that SafetyPoint really restores the project, and that the adapters'
/// fingerprints see a change made in EPLAN between the plan and the execution.
/// </summary>
[Trait("Size", "Large")]
public sealed class WriteSafetyTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task Q1_EveryWriteToTheCopyOutsideScratch_IsPolicyDeniedAndChangesNothing()
    {
        string project = host.Outside;
        Dictionary<string, (long Size, DateTime Written)> before = DesignFiles(project);
        int records = host.AuditRecords().Count;

        JsonElement[] errors =
        [
            await host.ErrorAsync("eplan_page_create", new() { ["project"] = project, ["pages"] = Json("""[{"name": "=GAA+A1&EFS1/60", "description": "Q1"}]"""), ["language"] = "zh_CN" }),
            await host.ErrorAsync("eplan_page_update", new() { ["project"] = project, ["pages"] = Json("""[{"name": "=GAA+A1&EFS1/1", "description": "Q1"}]"""), ["language"] = "zh_CN" }),
            await host.ErrorAsync("eplan_device_update", new() { ["project"] = project, ["tag"] = "=F+A2-KF1", ["new_tag"] = "=F+A2-KF19" }),
        ];

        Assert.All(errors, e => Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (e.GetProperty("reason").GetString(), e.GetProperty("execution").GetString())));
        Assert.Equal(before, DesignFiles(project));
        Assert.Equal(records, host.AuditRecords().Count); // refused before execution: nothing to audit (A17)
        Assert.Equal(ErrorReason.PageNotFound, (await host.ErrorAsync("eplan_page_get", new() { ["project"] = project, ["page"] = "=GAA+A1&EFS1/60" })).GetProperty("reason").GetString());
        Assert.Equal("=F+A2-KF1", (await host.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = "=F+A2-KF1" })).GetProperty("device").GetProperty("tag").GetString());
    }

    [Fact]
    public async Task Q2_ARenameThatFailsInsideTheTransaction_IsRolledBackAndTheCableKeepsItsTag()
    {
        // A17: a cable does not use the plant part, so EPLAN names it +A1-WE19; the read-back sees that and rolls back.
        string project = host.Writable;
        string[] before = Functions(await host.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = "+A1-WE1" }));
        int records = host.AuditRecords().Count;

        JsonElement error = await host.ErrorAsync("eplan_device_update", new() { ["project"] = project, ["tag"] = "+A1-WE1", ["new_tag"] = "=F+A1-WE19" });

        Assert.Equal(
            (ErrorReason.InvalidArgument, ExecutionState.RolledBack, "+A1-WE19"),
            (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("actual").GetString()));
        Assert.Equal(before, Functions(await host.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = "+A1-WE1" })));
        Assert.Equal(ErrorReason.DeviceNotFound, (await host.ErrorAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = "+A1-WE19" })).GetProperty("reason").GetString());
        Assert.Equal(
            [("execution_started", null), ("execution_result", ExecutionState.RolledBack)],
            host.AuditRecords().Skip(records).Select(r => (r["kind"]!.GetValue<string>(), r["execution"]?.GetValue<string>())));
        Assert.Empty(host.Suspensions());
    }

    [Fact]
    public async Task Q6_APageAddedWhereThePlannedOneGoes_MakesThePlanStaleAndNothingIsCreated()
    {
        string project = host.Writable;
        int number = await NextPageNumberAsync(project, "=GAA+A1&EFS1/");
        await using BridgeClient bridge = await BridgeAsync();
        var args = new PageCreateArgs { Project = project, Pages = [new PageCreateItem { Name = Page(number) }] };
        WritePlan plan = await bridge.InvokeAsync<PageCreateArgs, WritePlan>(OperationNames.PageCreatePlan, args, TestContext.Current.CancellationToken);

        // Someone else adds a page where the planned one goes: here this test's eplanmcp, on the same scratch copy.
        await host.CallAsync("eplan_page_create", new() { ["project"] = project, ["pages"] = Json($$"""[{"name": "{{Page(number + 1)}}"}]""") });
        BridgeCallException stale = await Assert.ThrowsAsync<BridgeCallException>(() => bridge.InvokeWriteAsync<PageCreateArgs, PageCreateData>(
            OperationNames.PageCreateExecute,
            new PageCreateArgs { Project = args.Project, Pages = args.Pages, Fingerprint = plan.Fingerprint },
            Guid.NewGuid().ToString("N"),
            TimeSpan.FromSeconds(60),
            null,
            TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (stale.Error.Reason, stale.Error.Execution));
        Assert.Equal(ErrorReason.PageNotFound, (await host.ErrorAsync("eplan_page_get", new() { ["project"] = project, ["page"] = Page(number) })).GetProperty("reason").GetString());
        Assert.Equal(Page(number + 1), (await host.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = Page(number + 1) })).GetProperty("page").GetProperty("name").GetString());

        static string Page(int n) => "=GAA+A1&EFS1/" + n.ToString(CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Q6_ADescriptionChangedAfterThePlan_MakesThePlanStaleAndTheLaterChangeStays()
    {
        string project = host.Writable;
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        await using BridgeClient bridge = await BridgeAsync();
        var args = new PageUpdateArgs { Project = project, Pages = [new PageUpdateItem { Name = "=GAA+A1&EFS1/1", Description = "Q6 plan " + stamp }], Language = "zh_CN" };
        WritePlan plan = await bridge.InvokeAsync<PageUpdateArgs, WritePlan>(OperationNames.PageUpdatePlan, args, TestContext.Current.CancellationToken);

        await host.CallAsync("eplan_page_update", new() { ["project"] = project, ["pages"] = Json($$"""[{"name": "=GAA+A1&EFS1/1", "description": "Q6 between {{stamp}}"}]"""), ["language"] = "zh_CN" });
        BridgeCallException stale = await Assert.ThrowsAsync<BridgeCallException>(() => bridge.InvokeWriteAsync<PageUpdateArgs, PageUpdateData>(
            OperationNames.PageUpdateExecute,
            new PageUpdateArgs { Project = args.Project, Pages = args.Pages, Language = args.Language, Fingerprint = plan.Fingerprint },
            Guid.NewGuid().ToString("N"),
            TimeSpan.FromSeconds(60),
            null,
            TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (stale.Error.Reason, stale.Error.Execution));
        JsonElement page = await host.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = "=GAA+A1&EFS1/1", ["language"] = "zh_CN" });
        Assert.Equal("Q6 between " + stamp, page.GetProperty("page").GetProperty("description").GetString());
    }

    /// <summary>
    /// The Host's own client to the Bridge in the EPLAN under test, for Q6: the plan and the execution are two calls, and
    /// the change goes in between. It bypasses the Host's policy, so the copy must be the scratch one.
    /// </summary>
    private async Task<BridgeClient> BridgeAsync()
    {
        Assert.StartsWith(WriteHost.ScratchFolder + Path.DirectorySeparatorChar, host.Writable, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        var bridge = new BridgeClient(new PipeBridgeDiscovery(), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
        await bridge.SelectAsync(eplan.ProcessId, TestContext.Current.CancellationToken);
        return bridge;
    }

    /// <summary>One above the highest page number in <paramref name="structure"/>, so a run never meets an earlier run's pages.</summary>
    private async Task<int> NextPageNumberAsync(string project, string structure)
    {
        int highest = 0;
        string? cursor = null;
        do
        {
            Dictionary<string, object?> args = new() { ["project"] = project, ["name_prefix"] = structure, ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }

            JsonElement list = await host.CallAsync("eplan_page_list", args);
            highest = list.GetProperty("pages").EnumerateArray()
                .Select(p => int.TryParse(p.GetProperty("name").GetString()![structure.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
                .Append(highest)
                .Max();
            cursor = list.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        return highest + 1;
    }

    /// <summary>Every function of a device as tag and page, sorted: the device as a rename would change it.</summary>
    private static string[] Functions(JsonElement device) =>
        [.. device.GetProperty("functions").EnumerateArray().Select(f => f.GetProperty("tag").GetString() + " @ " + f.GetProperty("page").GetString()).Order(StringComparer.Ordinal)];

    /// <summary>The design data of a project: its files without the user data EPLAN rewrites on any call (A17).</summary>
    private static Dictionary<string, (long Size, DateTime Written)> DesignFiles(string project) => NoSideEffectTests.ProjectFiles(project, withUserData: false);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
