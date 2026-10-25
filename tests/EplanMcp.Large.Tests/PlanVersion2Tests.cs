using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>LP-9 public compilation and application through the real stdio Host; no test-shaped definitions.</summary>
[Trait("Size", "Large")]
public sealed class PlanVersion2Tests(LiveEplan eplan)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LP9_LargeWrite_CompileGetApplyTask_VerifiesLocalPagesAndInitialBackup()
    {
        Assert.SkipWhen(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EPLANMCP_LARGE_FIXTURE")),
            "Run the LargeWrite case without EPLANMCP_LARGE_FIXTURE.");
        string project = await eplan.WriteProjectAsync();
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(project));
        Assert.True(ScratchPaths.IsInside(project, [WriteHost.ScratchFolder]));
        await RunAsync(project, acceptance: false);
    }

    [Fact]
    public async Task LP9_LargeFixture_1000Or3000Pages_CompileGetApplyTask_PrintsTimings()
    {
        string? fixture = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_FIXTURE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(fixture),
            "Set EPLANMCP_LARGE_FIXTURE to a 1,000- or 3,000-page scratch run copy, the only open project.");
        Assert.True(Path.IsPathFullyQualified(fixture));
        Assert.True(ScratchPaths.IsInside(fixture, [LiveEplan.ScratchRoot()]));
        JsonElement only = Assert.Single((await eplan.CallAsync("eplan_project_list")).GetProperty("projects").EnumerateArray());
        Assert.Equal(PlanSnapshotHashes.ProjectKey(fixture), PlanSnapshotHashes.ProjectKey(only.GetProperty("path").GetString()!));
        await RunAsync(only.GetProperty("path").GetString()!, acceptance: true);
    }

    private async Task RunAsync(string project, bool acceptance)
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(project, Token);
        int pageCount = names.Names.Count(n => n.Kind == "page");
        if (acceptance)
        {
            Assert.True(pageCount is 1000 or 3000);
        }
        PlanFunction[] device = await DeviceAsync(bridge, project, names);
        string oldName = device[0].Name;
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string newName = oldName[..oldName.LastIndexOf('-')] + "-LP9" + suffix;
        string[] devicePages = [.. device.Select(f => f.Page!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new()
        {
            Project = project,
            Pages = [.. devicePages]
        });
        int number = names.Names.Where(n => n.Kind == "page" && n.Name.StartsWith(DestructiveHost.Prefix, StringComparison.Ordinal))
            .Select(n => DestructiveHost.Number(n.Name)).DefaultIfEmpty().Max() + 100;
        string page = DestructiveHost.Prefix + number.ToString(CultureInfo.InvariantCulture);
        string home = Directory.CreateTempSubdirectory("eplanmcp-lp9-large-").FullName;
        var locations = new HostDataLocations(home);
        var folders = new BackupFolders(locations, TimeProvider.System);
        await File.WriteAllTextAsync(Path.Combine(home, "config.json"), JsonSerializer.Serialize(new
        {
            scratchRoots = new[] { LiveEplan.ScratchRoot() },
            writesOutsideScratch = "deny",
        }), Token);
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "LP9 stdio acceptance",
            Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
            Arguments = ["serve"],
            EnvironmentVariables = new Dictionary<string, string?> { ["EPLANMCP_HOME"] = home },
        });
        await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: Token);
        bool safeToClean = true;
        try
        {
            var watch = Stopwatch.StartNew();
            JsonElement compiled = await CallAsync(client, WorkflowTools.PlanTool, new()
            {
                ["project"] = project,
                ["steps"] = new object[]
                {
                    new { op = "page.create", args = new { pages = new[] { new { name = page, description = "LP9 created" } }, language = "en_US" } },
                    new { op = "page.update", args = new { pages = new[] { new { name = new { from_step = 1, page }, description = "LP9 updated" } }, language = "en_US" } },
                    new { op = "device.update", args = new { tag = oldName, new_tag = newName } },
                },
            });
            double compileSeconds = watch.Elapsed.TotalSeconds;
            string id = compiled.GetProperty("planId").GetString()!;
            List<JsonElement> entries = await InspectAsync(client, id);
            Assert.Empty(Value(entries, 1, "readScope").GetProperty("pages").EnumerateArray());
            Assert.Equal(new[] { page }, Value(entries, 2, "readScope").GetProperty("pages").EnumerateArray().Select(p => p.GetString()));
            Assert.Equal(devicePages, Value(entries, 3, "readScope").GetProperty("pages").EnumerateArray().Select(p => p.GetString()));
            JsonElement modify = Value(entries, 3, "modifyScope");
            Assert.Empty(modify.GetProperty("pages").EnumerateArray());
            Assert.Equal(device.Length, modify.GetProperty("locators").GetArrayLength());
            JsonElement dry = await CallAsync(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = id,
                ["dry_run"] = true
            });
            Assert.Equal("not_started", dry.GetProperty("execution").GetString());

            watch.Restart();
            safeToClean = false;
            JsonElement receipt = await CallAsync(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = id,
                ["wait_seconds"] = 0
            });
            string taskId = receipt.GetProperty("taskId").GetString()!;
            JsonElement task;
            do
            {
                task = await CallAsync(client, PlanApplyTools.TaskTool, new()
                {
                    ["task_id"] = taskId,
                    ["wait_seconds"] = 110
                });
                Assert.True(watch.Elapsed < TimeSpan.FromMinutes(16), "The plan task did not settle within its total budget.");
            }
            while (task.GetProperty("completedAt").ValueKind == JsonValueKind.Null);
            safeToClean = task.GetProperty("status").GetString() != "unknown";
            double applySeconds = watch.Elapsed.TotalSeconds;
            Print(FormattableString.Invariant($"pages={pageCount}; compile={compileSeconds:F3}s; apply/backup={applySeconds:F3}s; task={task.GetProperty("status").GetString()}"));
            JsonElement saved = await CallAsync(client, PlanTools.GetTool, new()
            {
                ["plan_id"] = id
            });
            Assert.True(task.GetProperty("status").GetString() == "succeeded", task.GetRawText() + "\n" + saved.GetRawText());
            Assert.Equal("committed", saved.GetProperty("result").GetProperty("execution").GetString());
            Assert.Equal("passed", saved.GetProperty("result").GetProperty("verification").GetString());
            Assert.Equal(3, saved.GetProperty("summary").GetProperty("completedSteps").GetInt32());
            JsonElement readBack = await CallAsync(client, "eplan_page_get", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["response_format"] = "detailed",
                ["language"] = "en_US"
            });
            Assert.Contains("LP9 updated", readBack.GetRawText(), StringComparison.Ordinal);
            PlanNamesData after = await new PlanNamesReader(bridge).ReadAsync(project, Token);
            Assert.Equal(pageCount + 1, after.Names.Count(n => n.Kind == "page"));
            PlanFunctionsData renamed = await FunctionsAsync(bridge, project, [oldName, newName]);
            Assert.Equal(device.Select(f => (f.Type, f.Oid, f.Page)).Order(), renamed.Functions.Select(f => (f.Type, f.Oid, f.Page)).Order());
            Assert.All(renamed.Functions, f => Assert.Equal(newName, f.Name));

            PlanBackup backup = PlanJson.Read<PlanBackup>(WriteHost.AuditRecordsIn(home).Single(r => r["kind"]?.GetValue<string>() == "plan_result"
                && r["planId"]?.GetValue<string>() == id)["result"]!["backups"]![0]!.ToJsonString());
            Assert.True(backup.Usable);
            Assert.Equal(pageCount, backup.Pages);
            Assert.True(File.Exists(backup.Path));
            LiveSnapshots.Snapshot copy = await LiveSnapshots.TakeAsync(bridge, new()
            {
                Project = backup.Path,
                OpenReadOnly = true,
                Pages = [.. devicePages, page]
            });
            Assert.Contains(copy.Chunks.SelectMany(c => c.MissingPages), p => p == page);
            Assert.Equal(before.Pages.Select(p => (p.Name, p.Hash)).Order(), copy.Pages.Select(p => (p.Name, p.Hash)).Order());
            Print("Initial backup verified: " + backup.Path);
        }
        finally
        {
            // An unsettled write must never race cleanup; retain its evidence and scratch copy for inspection.
            if (safeToClean)
            {
                PlanFunctionsData current = await FunctionsAsync(bridge, project, [newName]);
                if (current.Functions.Count > 0)
                {
                    Assert.Equal(device.Select(f => (f.Type, f.Oid)).Order(), current.Functions.Select(f => (f.Type, f.Oid)).Order());
                    var restore = new DeviceUpdateArgs { Project = project, Tag = newName, NewTag = oldName };
                    restore.Fingerprint = (await bridge.InvokeAsync<DeviceUpdateArgs, WritePlan>(OperationNames.DeviceUpdatePlan, restore, Token)).Fingerprint;
                    await bridge.InvokeWriteAsync<DeviceUpdateArgs, JsonElement>(OperationNames.DeviceUpdateExecute, restore, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(120), null, Token);
                    PlanFunctionsData restored = await FunctionsAsync(bridge, project, [oldName, newName]);
                    Assert.Equal(device.Select(f => (f.Type, f.Oid, f.Page)).Order(), restored.Functions.Select(f => (f.Type, f.Oid, f.Page)).Order());
                    Assert.All(restored.Functions, f => Assert.Equal(oldName, f.Name));
                }
                if ((await new PlanNamesReader(bridge).ReadAsync(project, Token)).Names.Any(n => n.Kind == "page" && n.Name == page))
                {
                    var delete = new PageDeleteArgs { Project = project, Pages = [page] };
                    delete.Fingerprint = (await bridge.InvokeAsync<PageDeleteArgs, WritePlan>(OperationNames.PageDeletePlan, delete, Token)).Fingerprint;
                    delete.Backup = new()
                    {
                        Path = folders.Create(project),
                        MaxSeconds = 60
                    };
                    await bridge.InvokeWriteAsync<PageDeleteArgs, JsonElement>(OperationNames.PageDeleteExecute, delete, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(120), null, Token);
                }
                Assert.DoesNotContain((await new PlanNamesReader(bridge).ReadAsync(project, Token)).Names, n => n.Name == page);
            }
            Print("Retained test audit and backups: " + home);
        }
    }

    private static async Task<PlanFunction[]> DeviceAsync(BridgeClient bridge, string project, PlanNamesData names)
    {
        // device.update renames only devices whose functions all carry the tag itself, without ":suffix" siblings.
        var suffixed = names.Names.Where(n => n.Kind == "function" && n.Name.Contains(':', StringComparison.Ordinal))
            .Select(n => n.Name[..n.Name.IndexOf(':', StringComparison.Ordinal)]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int attempts = 0;
        foreach (string[] batch in names.Names.Where(n => n.Kind == "function" && n.Name.Contains("-K", StringComparison.Ordinal)
            && !n.Name.Contains(':', StringComparison.Ordinal) && !suffixed.Contains(n.Name)).Select(n => n.Name)
            .OrderBy(n => n != "=F+A2-KF1").ThenBy(n => n, StringComparer.Ordinal).Chunk(200))
        {
            PlanFunctionsData functions = await FunctionsAsync(bridge, project, batch);
            foreach (PlanFunction[] selected in functions.Functions.GroupBy(f => f.Name, StringComparer.Ordinal)
                .Where(g => g.Any(f => f.IsMain) && g.All(f => !string.IsNullOrEmpty(f.Page)) && g.Count() <= 200
                    && g.Select(f => f.Page).Distinct(StringComparer.Ordinal).Count() <= 50)
                .Select(g => g.ToArray()))
            {
                // Function names such as "-QA1-K1" may not be device tags; keep only a tag that device.update.plan accepts (read-only).
                try
                {
                    await bridge.InvokeAsync<DeviceUpdateArgs, WritePlan>(OperationNames.DeviceUpdatePlan, new()
                    {
                        Project = project,
                        Tag = selected[0].Name,
                        NewTag = "-LP9_PROBE" + Guid.NewGuid().ToString("N")[..8],
                    }, Token);
                    return selected;
                }
                catch (BridgeCallException) when (++attempts < 20)
                {
                    // Try the next candidate.
                }
            }
        }
        throw new InvalidOperationException("The scratch fixture needs a placed K device with a complete bounded closure.");
    }

    private static async Task<PlanFunctionsData> FunctionsAsync(BridgeClient bridge, string project, IEnumerable<string> names)
    {
        PlanFunctionsData result = await bridge.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
            new()
            {
                Project = project,
                Names = [.. names]
            }, Token);
        Assert.True(result.Complete);
        return result;
    }

    private static async Task<List<JsonElement>> InspectAsync(McpClient client, string id)
    {
        var entries = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var args = new Dictionary<string, object?> { ["plan_id"] = id, ["response_format"] = "detailed", ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }
            JsonElement data = await CallAsync(client, PlanTools.GetTool, args);
            Assert.Equal("2", data.GetProperty("plan").GetProperty("compilerVersion").GetString());
            entries.AddRange(data.GetProperty("entries").EnumerateArray().Select(e => e.Clone()));
            cursor = data.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);
        return entries;
    }

    private static JsonElement Value(IEnumerable<JsonElement> entries, int step, string key)
    {
        JsonElement[] matching = [.. entries.Where(e => e.GetProperty("section").GetString() == "step"
            && e.GetProperty("index").GetInt32() == step && e.GetProperty("key").GetString() == key)];
        if (!matching[0].TryGetProperty("fragmentIndex", out _))
        {
            return Assert.Single(matching).GetProperty("value");
        }
        Assert.Equal(matching[0].GetProperty("fragmentCount").GetInt32(), matching.Length);
        return JsonSerializer.Deserialize<JsonElement>(string.Concat(matching.OrderBy(e => e.GetProperty("fragmentIndex").GetInt32()).Select(e => e.GetProperty("value").GetString())));
    }

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await client.CallToolAsync(tool, args, cancellationToken: Token);
        Assert.True(result.IsError != true, tool + ": " + WriteHost.Text(result));
        return result.StructuredContent!.Value;
    }

    private static void Print(string message) => TestContext.Current.TestOutputHelper!.WriteLine(message);
}
