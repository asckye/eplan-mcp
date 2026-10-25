using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;

namespace EplanMcp.Large.Tests;

/// <summary>P23's public tools over the same real stdio Host as the existing Large write tests.</summary>
[Trait("Size", "Large")]
[Trait("Tool", PlanApplyTools.ApplyTool)]
[Trait("Tool", PlanApplyTools.TaskTool)]
public sealed class PlanApplyTests(WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task P23_LargeWrite_WorkflowApplyTaskGet_TwoStepsVerifiedAndInitialBackupOpens()
    {
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(host.Writable));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable });
        string page = NextPage(before);
        try
        {
            JsonElement compiled = await host.CallAsync(WorkflowTools.PlanTool, Request(host.Writable, page));
            string planId = compiled.GetProperty("planId").GetString()!;
            JsonElement receipt = await host.CallAsync(PlanApplyTools.ApplyTool, new() { ["plan_id"] = planId, ["wait_seconds"] = 0 });
            string taskId = receipt.GetProperty("taskId").GetString()!;
            JsonElement task = await host.CallAsync(PlanApplyTools.TaskTool, new() { ["task_id"] = taskId, ["wait_seconds"] = 110 });
            while (task.GetProperty("completedAt").ValueKind == JsonValueKind.Null)
            {
                task = await host.CallAsync(PlanApplyTools.TaskTool, new() { ["task_id"] = taskId, ["wait_seconds"] = 110 });
            }
            Assert.Equal("succeeded", task.GetProperty("status").GetString());
            Assert.Equal(planId, task.GetProperty("result").GetProperty("planId").GetString());
            JsonElement saved = await host.CallAsync(PlanTools.GetTool, new() { ["plan_id"] = planId });
            Assert.Equal("committed", saved.GetProperty("result").GetProperty("execution").GetString());
            Assert.Equal("passed", saved.GetProperty("result").GetProperty("verification").GetString());
            JsonElement readBack = await host.CallAsync("eplan_page_get", new() { ["project"] = host.Writable, ["page"] = page, ["response_format"] = "detailed" });
            Assert.Contains("P23 updated", readBack.GetRawText(), StringComparison.Ordinal);
            LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable });
            Assert.Equal(before.Pages.Count + 1, after.Pages.Count);
            Assert.Contains(after.Pages, p => p.Name == page);
            List<System.Text.Json.Nodes.JsonObject> audit = host.AuditRecords();
            Assert.Equal(2, audit.Count(r => r["kind"]?.GetValue<string>() == "execution_result" && r["planId"]?.GetValue<string>() == planId));
            Assert.Single(audit, r => r["kind"]?.GetValue<string>() == "plan_approval" && r["planId"]?.GetValue<string>() == planId && r["policy"]?.GetValue<string>() == "Allow");
            string backup = audit.Single(r => r["kind"]?.GetValue<string>() == "plan_result" && r["planId"]?.GetValue<string>() == planId)["result"]!["backups"]![0]!["path"]!.GetValue<string>();
            Assert.True(File.Exists(backup));
            Assert.EndsWith(".elk", backup, StringComparison.OrdinalIgnoreCase);
            LiveSnapshots.Snapshot copy = await LiveSnapshots.TakeAsync(bridge, new() { Project = backup, OpenReadOnly = true });
            Assert.Equal(before.Pages.Select(p => (p.Name, p.Hash)).OrderBy(p => p.Name, StringComparer.Ordinal),
                copy.Pages.Select(p => (p.Name, p.Hash)).OrderBy(p => p.Name, StringComparer.Ordinal));
            Assert.DoesNotContain(copy.Pages, p => p.Name == page);
            TestContext.Current.TestOutputHelper!.WriteLine($"Two steps committed and verified; initial backup retained: {backup}");
        }
        finally
        {
            // Match the existing Large orchestrator tests: clean up only this test's page on LargeWrite.
            LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable });
            if (after.Pages.Any(p => p.Name == page))
            {
                var args = new PageDeleteArgs { Project = host.Writable, Pages = [page] };
                WritePlan cleanup = await bridge.InvokeAsync<PageDeleteArgs, WritePlan>(OperationNames.PageDeletePlan, args, TestContext.Current.CancellationToken);
                args.Fingerprint = cleanup.Fingerprint;
                var folders = new BackupFolders(new HostDataLocations(host.Home), TimeProvider.System);
                args.Backup = new() { Path = folders.Create(host.Writable), MaxSeconds = 60 };
                await bridge.InvokeWriteAsync<PageDeleteArgs, JsonElement>(OperationNames.PageDeleteExecute, args, Guid.NewGuid().ToString("N"),
                    TimeSpan.FromSeconds(120), null, TestContext.Current.CancellationToken);
            }
        }
    }

    [Fact]
    public async Task P23_LargeOutside_DenyConsumesPlan_WithoutApprovalBackupOrWrite()
    {
        Assert.Equal("LargeOutside", Path.GetFileName(host.Outside));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Outside });
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(host.Outside);
        string[] backups = BackupFiles();
        JsonElement compiled = await host.CallAsync(WorkflowTools.PlanTool, Request(host.Outside, NextPage(before)));
        string planId = compiled.GetProperty("planId").GetString()!;
        JsonElement error = await host.ErrorAsync(PlanApplyTools.ApplyTool, new() { ["plan_id"] = planId });
        Assert.Equal(ErrorReason.PolicyDenied, error.GetProperty("reason").GetString());
        Assert.Equal("not_started", error.GetProperty("execution").GetString());
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(host.Outside).OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(backups, BackupFiles());
        Assert.DoesNotContain(host.AuditRecords(), r => r["planId"]?.GetValue<string>() == planId && r["kind"]?.GetValue<string>() is "plan_approval" or "execution_started");
        JsonElement saved = await host.CallAsync(PlanTools.GetTool, new() { ["plan_id"] = planId });
        Assert.Equal("not_started", saved.GetProperty("status").GetString());
        Assert.NotNull(saved.GetProperty("taskId").GetString());
        Assert.Equal(ErrorReason.InvalidArgument, (await host.ErrorAsync(PlanApplyTools.ApplyTool, new() { ["plan_id"] = planId })).GetProperty("reason").GetString());
    }

    private string[] BackupFiles() => Directory.Exists(Path.Combine(host.Home, "backups"))
        ? [.. Directory.GetFileSystemEntries(Path.Combine(host.Home, "backups"), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)] : [];

    private static string NextPage(LiveSnapshots.Snapshot snapshot) => DestructiveHost.Prefix
        + (snapshot.Pages.Where(p => p.Name.StartsWith(DestructiveHost.Prefix, StringComparison.Ordinal)).Select(p => DestructiveHost.Number(p.Name)).DefaultIfEmpty().Max() + 100).ToString(CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Request(string project, string page) => new()
    {
        ["project"] = project,
        ["steps"] = new object[]
        {
            new { op = "page.create", args = new { pages = new[] { new { name = page, description = "P23 created" } }, language = "en_US" } },
            new { op = "page.update", args = new { pages = new[] { new { name = new { from_step = 1, page }, description = "P23 updated" } }, language = "en_US" } },
        },
    };
}
