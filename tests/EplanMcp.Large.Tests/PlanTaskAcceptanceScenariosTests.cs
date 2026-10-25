using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tools;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlanTaskAcceptanceScenariosTests(LiveEplan eplan)
{
    private static readonly string[] _approvalPages = ["+P25/10"];
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task P25_CreateOpenCompileReviewApplyTask_RealModificationVerifiedAndInitialBackupOpens()
    {
        await using PlanTaskAcceptanceSession run = await PlanTaskAcceptanceSession.StartAsync(eplan);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(run.ProcessId);
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new() { Project = run.Project });
        const string page = "+P25/10";
        var steps = new List<object>
        {
            new { op = "page.create", args = new { pages = new[] { new { name = page, description = "P25 created" } }, language = "en_US" } },
            new { op = "page.update", args = new { pages = new[] { new { name = new { from_step = 1, page }, description = "P25 verified" } }, language = "en_US" } }
        };
        (string Old, string New, PlanFunction[] Functions)? rename = await RenameAsync(bridge, run.Project);
        if (rename is { } device)
        {
            steps.Add(new { op = "device.update", args = new { tag = device.Old, new_tag = device.New } });
        }
        string id = await run.CompileAsync([.. steps]);
        JsonElement review = await run.CallAsync(PlanTools.GetTool, new()
        {
            ["plan_id"] = id,
            ["response_format"] = "detailed",
            ["limit"] = 200
        });
        Assert.Equal("2", review.GetProperty("plan").GetProperty("compilerVersion").GetString());
        Assert.Equal(steps.Count, review.GetProperty("summary").GetProperty("totalSteps").GetInt32());
        JsonElement receipt = await run.WriteAsync(PlanApplyTools.ApplyTool, new()
        {
            ["plan_id"] = id,
            ["wait_seconds"] = 0
        });
        JsonElement task = await run.WaitAsync(receipt.GetProperty("taskId").GetString()!);
        Assert.Equal("succeeded", task.GetProperty("status").GetString());
        JsonElement saved = await run.CallAsync(PlanTools.GetTool, new() { ["plan_id"] = id });
        Assert.Equal("committed", saved.GetProperty("result").GetProperty("execution").GetString());
        Assert.Equal("passed", saved.GetProperty("result").GetProperty("verification").GetString());
        Assert.Equal(steps.Count, saved.GetProperty("summary").GetProperty("completedSteps").GetInt32());
        Assert.Equal("P25 verified", (await PageAsync(run, page)).GetProperty("page").GetProperty("description").GetString());
        if (rename is { } renamed)
        {
            PlanFunctionsData actual = await FunctionsAsync(bridge, run.Project, [renamed.Old, renamed.New]);
            Assert.Equal(renamed.Functions.Select(f => (f.Type, f.Oid, f.Page)).Order(), actual.Functions.Select(f => (f.Type, f.Oid, f.Page)).Order());
            Assert.All(actual.Functions, f => Assert.Equal(renamed.New, f.Name));
        }
        PlanBackup backup = PlanJson.Read<PlanBackup>(run.Audit().Single(r => r["kind"]?.GetValue<string>() == "plan_result"
            && r["planId"]?.GetValue<string>() == id)["result"]!["backups"]![0]!.ToJsonString());
        Assert.True(backup.Usable);
        Assert.True(File.Exists(backup.Path));
        Assert.Equal(before.Pages.Count, backup.Pages);
        LiveSnapshots.Snapshot copy = await LiveSnapshots.TakeAsync(bridge, new() { Project = backup.Path, OpenReadOnly = true });
        Assert.Equal(before.Pages.Select(p => (p.Name, p.Hash)).Order(), copy.Pages.Select(p => (p.Name, p.Hash)).Order());
        Assert.DoesNotContain(copy.Pages, p => p.Name == page);
        Assert.Equal(steps.Count, run.Audit().Count(r => r["kind"]?.GetValue<string>() == "execution_result" && r["planId"]?.GetValue<string>() == id));
    }

    [Fact]
    public async Task P25_Scenario4_ManualScopeChange_PlanStaleListsPageAndApplyWritesNothing()
    {
        await using PlanTaskAcceptanceSession run = await PlanTaskAcceptanceSession.StartAsync(eplan);
        await run.CreatePageAsync("+P25/10");
        string id = await run.CompileAsync(Update("+P25/10"));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(run.ProcessId);
        var change = new PageUpdateArgs
        {
            Project = run.Project,
            Language = "en_US",
            Pages = [new() { Name = "+P25/10", Description = "P25 manual change" }]
        };
        change.Fingerprint = (await bridge.InvokeAsync<PageUpdateArgs, WritePlan>(OperationNames.PageUpdatePlan, change, Token)).Fingerprint;
        run.BeginExternalWrite();
        try
        {
            await bridge.InvokeWriteAsync<PageUpdateArgs, PageUpdateData>(OperationNames.PageUpdateExecute, change,
                Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(60), null, Token);
        }
        catch (BridgeCallException failure) when (failure.Error.Execution is ExecutionState.NotStarted or ExecutionState.RolledBack or ExecutionState.Committed)
        {
            run.ExternalWriteSettled();
            throw;
        }
        run.ExternalWriteSettled();
        LiveSnapshots.Snapshot manual = await LiveSnapshots.TakeAsync(bridge, new() { Project = run.Project });
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(run.Project);
        string[] backups = run.Backups();
        JsonElement refused = await run.ApplyErrorAsync(id);
        Assert.Equal("not_started", refused.GetProperty("execution").GetString());
        JsonElement error = refused.GetProperty("error");
        Assert.Equal(ErrorReason.PlanStale, error.GetProperty("reason").GetString());
        Assert.Contains(error.GetProperty("metadata").GetProperty("conflicts").EnumerateArray(), c => c.GetProperty("scope").GetString() == "page:+P25/10");
        LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, new() { Project = run.Project });
        Assert.Equal(manual.Pages.Select(p => (p.Name, p.Hash)).Order(), after.Pages.Select(p => (p.Name, p.Hash)).Order());
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(run.Project).OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(backups, run.Backups());
        Assert.DoesNotContain(run.Audit(), r => r["planId"]?.GetValue<string>() == id && r["kind"]?.GetValue<string>() == "execution_started");
    }

    [Fact]
    public async Task P25_Scenario5_SelectionChangedToAnotherScratchProject_ApprovedPageRemainsTarget()
    {
        await using PlanTaskAcceptanceSession run = await PlanTaskAcceptanceSession.StartAsync(eplan);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(run.ProcessId);
        LiveSnapshots.Snapshot fixtureBefore = await LiveSnapshots.TakeAsync(bridge, new() { Project = eplan.ProjectPath });
        Dictionary<string, Dictionary<string, string>> copyFiles = run.InitialCopies.ToDictionary(
            p => p, LiveSnapshots.DesignFiles, StringComparer.OrdinalIgnoreCase);
        await run.CreatePageAsync("+P25/10");
        await run.CreatePageAsync("+P25/11");
        await run.AssertCurrentAsync(eplan.ProjectPath);
        string id = await run.CompileAsync(Update("+P25/10"));
        await run.AssertCurrentAsync(eplan.ProjectPath);
        // A46: later-sorting closed projects preserve selection; reopening an open project blocks EPLAN.
        // Close only the known current copies until O is first, keeping the explicitly compiled target T open.
        string other = await run.ChangeSelectionToOtherProjectAsync();
        LiveSnapshots.Snapshot otherBefore = await LiveSnapshots.TakeAsync(bridge, new() { Project = other });
        Dictionary<string, string> otherFiles = LiveSnapshots.DesignFiles(other);
        JsonElement receipt = await run.WriteAsync(PlanApplyTools.ApplyTool, new() { ["plan_id"] = id, ["wait_seconds"] = 0 });
        Assert.Equal("succeeded", (await run.WaitAsync(receipt.GetProperty("taskId").GetString()!)).GetProperty("status").GetString());
        Assert.Equal("P25 verified", (await PageAsync(run, "+P25/10")).GetProperty("page").GetProperty("description").GetString());
        Assert.Equal("P25 setup", (await PageAsync(run, "+P25/11")).GetProperty("page").GetProperty("description").GetString());
        await run.AssertCurrentAsync(other);
        Assert.Equal(otherFiles.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(other).OrderBy(p => p.Key, StringComparer.Ordinal));
        foreach (var copy in copyFiles)
        {
            Assert.Equal(WithoutProjectInfo(copy.Value), WithoutProjectInfo(LiveSnapshots.DesignFiles(copy.Key)));
        }
        LiveSnapshots.Snapshot otherAfter = await LiveSnapshots.TakeAsync(bridge, new() { Project = other });
        Assert.Equal(otherBefore.Pages.Select(p => (p.Name, p.Hash)).Order(), otherAfter.Pages.Select(p => (p.Name, p.Hash)).Order());
        await run.RestoreInitialAsync();
        foreach (var copy in copyFiles)
        {
            Assert.Equal(WithoutProjectInfo(copy.Value), WithoutProjectInfo(LiveSnapshots.DesignFiles(copy.Key)));
        }
        LiveSnapshots.Snapshot fixtureAfter = await LiveSnapshots.TakeAsync(bridge, new() { Project = eplan.ProjectPath });
        Assert.Equal(fixtureBefore.Pages.Select(p => (p.Name, p.Hash)).Order(), fixtureAfter.Pages.Select(p => (p.Name, p.Hash)).Order());
    }

    [Fact]
    public async Task P25_ApprovalRequired_ReceiptAndTaskShowWaiting_ExpiryOrDenialLeavesProjectUnchanged()
    {
        await using PlanTaskAcceptanceSession run = await PlanTaskAcceptanceSession.StartAsync(eplan);
        await run.CreatePageAsync("+P25/10");
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(run.Project);
        string id = await run.CompileAsync(new { op = "page.delete", args = new { pages = _approvalPages } });
        JsonElement receipt = await run.WriteAsync(PlanApplyTools.ApplyTool, new() { ["plan_id"] = id, ["wait_seconds"] = 0 });
        string taskId = receipt.GetProperty("taskId").GetString()!;
        JsonElement terminal;
        try
        {
            var watch = Stopwatch.StartNew();
            JsonElement waiting;
            do
            {
                waiting = await run.CallAsync(PlanApplyTools.TaskTool, new() { ["task_id"] = taskId, ["wait_seconds"] = 0 });
                if (waiting.GetProperty("status").GetString() != "waiting_approval")
                {
                    await Task.Delay(100, Token);
                }
            }
            while (waiting.GetProperty("status").GetString() is "queued" or "running" && watch.Elapsed < TimeSpan.FromSeconds(20));
            Assert.Equal("waiting_approval", waiting.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, waiting.GetProperty("result").ValueKind);
            Assert.Equal(0, waiting.GetProperty("progress").GetProperty("completedSteps").GetInt32());
            Assert.Empty(run.Backups());
        }
        finally
        {
            // Like the attended evals, leave the real channel to the engineer or its configured timeout.
            // No test clicks, closes or answers an approval dialog, even on assertion failure.
            terminal = await run.WaitAsync(taskId);
        }
        // An attending engineer may approve in the real channel; then the Destructive plan runs after its initial backup.
        if (terminal.GetProperty("status").GetString() == "succeeded")
        {
            Assert.Contains(run.Audit(), r => r["planId"]?.GetValue<string>() == id && r["kind"]?.GetValue<string>() == "plan_approval"
                && r["decision"]?.GetValue<string>() == "granted");
            Assert.NotEmpty(run.Backups());
            return;
        }
        Assert.Equal("failed", terminal.GetProperty("status").GetString());
        Assert.True(terminal.GetProperty("error").GetProperty("reason").GetString() is ErrorReason.ApprovalExpired or ErrorReason.ApprovalDenied);
        Assert.Empty(run.Backups());
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(run.Project).OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.DoesNotContain(run.Audit(), r => r["planId"]?.GetValue<string>() == id && r["kind"]?.GetValue<string>() == "execution_started");
    }

    [Fact]
    public async Task P25_Scenario8_UnavailableDefaultRenumberScheme_ReturnsCapabilityMissingWithMissing()
    {
        await using PlanTaskAcceptanceSession run = await PlanTaskAcceptanceSession.StartAsync(eplan);
        JsonElement error = await run.ErrorAsync(WorkflowTools.PlanTool, new()
        {
            ["project"] = run.Project,
            ["steps"] = new object[] { new { op = "device.renumber", args = new { pages = _approvalPages } } }
        });
        Assert.Equal(ErrorReason.CapabilityMissing, error.GetProperty("reason").GetString());
        Assert.Contains(error.GetProperty("metadata").GetProperty("missing").EnumerateArray(), m => m.GetString() == "plan:default_device_scheme");
        Assert.DoesNotContain(run.Audit(), r => r["kind"]?.GetValue<string>() == "plan_compiled");
    }

    // A46: closing a project rewrites its ProjectInfo.xml; page content is compared separately.
    private static KeyValuePair<string, string>[] WithoutProjectInfo(Dictionary<string, string> files) =>
        [.. files.Where(p => !string.Equals(p.Key, "ProjectInfo.xml", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Key, StringComparer.Ordinal)];

    private static object Update(string page) => new
    {
        op = "page.update",
        args = new { pages = new[] { new { name = page, description = "P25 verified" } }, language = "en_US" }
    };

    private static Task<JsonElement> PageAsync(PlanTaskAcceptanceSession run, string page) => run.CallAsync("eplan_page_get", new()
    {
        ["project"] = run.Project,
        ["page"] = page,
        ["language"] = "en_US",
        ["response_format"] = "detailed"
    });

    private static async Task<(string Old, string New, PlanFunction[] Functions)?> RenameAsync(BridgeClient bridge, string project)
    {
        PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(project, Token);
        if (names.Names.All(n => n.Kind != "function"))
        {
            TestContext.Current.TestOutputHelper!.WriteLine("The basic project has no device; page.create -> page.update closes the real modification loop.");
            return null;
        }
        // Match PlanVersion2Tests: avoid terminal/PLC suffix siblings outside the supported rename closure.
        HashSet<string> suffixed = names.Names.Where(n => n.Kind == "function" && n.Name.Contains(':', StringComparison.Ordinal))
            .Select(n => n.Name[..n.Name.IndexOf(':', StringComparison.Ordinal)]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] devices = [.. names.Names.Where(n => n.Kind == "function" && !n.Name.Contains(':', StringComparison.Ordinal)
            && !suffixed.Contains(n.Name)).Select(n => n.Name)];
        foreach (string[] batch in devices.Chunk(200))
        {
            PlanFunctionsData functions = await FunctionsAsync(bridge, project, batch);
            foreach (var device in functions.Functions.GroupBy(f => f.Name, StringComparer.Ordinal).Where(g => g.Any(f => f.IsMain)
                && g.All(f => !string.IsNullOrEmpty(f.Page)) && g.Count() <= 200 && g.Select(f => f.Page).Distinct(StringComparer.Ordinal).Count() <= 50))
            {
                string renamed = device.Key[..(device.Key.LastIndexOf('-') + 1)] + "P25" + Guid.NewGuid().ToString("N")[..8];
                try
                {
                    await bridge.InvokeAsync<DeviceUpdateArgs, WritePlan>(OperationNames.DeviceUpdatePlan,
                        new() { Project = project, Tag = device.Key, NewTag = renamed }, Token);
                    return (device.Key, renamed, [.. device]);
                }
                catch (BridgeCallException error) when (error.Error.Reason == ErrorReason.DeviceNotFound)
                {
                    // A function name may not be a device tag; the next candidate is still only a read-only probe.
                }
            }
        }
        Assert.Fail("The basic project has devices but none has a supported placed closure for device.update.");
        return null;
    }

    private static async Task<PlanFunctionsData> FunctionsAsync(BridgeClient bridge, string project, IEnumerable<string> names)
    {
        PlanFunctionsData data = await bridge.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
            new() { Project = project, Names = [.. names] }, Token);
        Assert.True(data.Complete);
        return data;
    }
}
