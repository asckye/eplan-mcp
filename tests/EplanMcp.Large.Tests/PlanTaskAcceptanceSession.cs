using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>One stdio session with disposable projects; A46 navigation never touches approval UI.</summary>
internal sealed class PlanTaskAcceptanceSession : IAsyncDisposable
{
    private readonly string _folder = Path.Combine(LiveEplan.ScratchRoot(), "p25-" + Guid.NewGuid().ToString("N"));
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-p25-large-").FullName;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly List<string> _created = [];
    private readonly List<string> _closed = [];
    private McpClient? _client;
    private OpenProject[] _initial = [];
    private string? _fixture;
    private bool _navigationFailed;
    private bool _safeToClean = true;
    public string Project { get; private set; } = string.Empty;
    public string[] InitialCopies => [.. _initial.Select(p => p.Path)];
    public int ProcessId { get; private set; }
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<PlanTaskAcceptanceSession> StartAsync(LiveEplan eplan)
    {
        string? basic = Environment.GetEnvironmentVariable(ProjectCreateTests.BasicProjectVariable)
            ?? ProjectCreateTests.Find("IEC_bas003_sample_project.zw9") ?? ProjectCreateTests.Find("GB_bas001.zw9");
        Assert.SkipWhen(basic is null, "Set EPLANMCP_LARGE_BASIC_PROJECT to an EPLAN sample basic project (.zw9).");
        var run = new PlanTaskAcceptanceSession();
        try
        {
            Directory.CreateDirectory(run._folder);
            string imports = Directory.CreateDirectory(Path.Combine(run._home, "imports")).FullName;
            File.Copy(basic!, Path.Combine(imports, "basic.zw9"));
            await File.WriteAllTextAsync(Path.Combine(run._home, "config.json"), JsonSerializer.Serialize(new
            {
                scratchRoots = new[] { LiveEplan.ScratchRoot() },
                writesOutsideScratch = "deny",
                approvalTimeoutSeconds = 60
            }), Token);
            run._client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "P25 complete task acceptance",
                Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
                Arguments = ["serve"],
                EnvironmentVariables = new Dictionary<string, string?> { ["EPLANMCP_HOME"] = run._home }
            }), cancellationToken: Token);
            JsonElement status = await run.CallAsync("eplan_session_status", []);
            run.ProcessId = status.GetProperty("eplan").GetProperty("processId").GetInt32();
            Assert.Equal(eplan.ProcessId, run.ProcessId);
            await run.CaptureInitialAsync(eplan.ProjectPath);
            run.Project = await run.CreateProjectAsync("P25t_" + run._suffix);
            await run.OpenClosedAsync(run.Project);
            await run.AssertCurrentAsync(run._fixture!);
            return run;
        }
        catch
        {
            await run.DisposeAsync();
            throw;
        }
    }

    public async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await RawAsync(tool, args);
        Assert.True(result.IsError != true, tool + ": " + WriteHost.Text(result));
        return result.StructuredContent!.Value;
    }

    public async Task<JsonElement> ErrorAsync(string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await RawAsync(tool, args);
        Assert.True(result.IsError, tool + " unexpectedly succeeded.");
        return result.StructuredContent!.Value.GetProperty("error");
    }

    public async Task<JsonElement> WriteAsync(string tool, Dictionary<string, object?> args)
    {
        _safeToClean = false;
        CallToolResult result = await RawAsync(tool, args);
        JsonElement data = result.StructuredContent!.Value;
        if (data.TryGetProperty("execution", out JsonElement execution))
        {
            _safeToClean = execution.GetString() != "unknown";
        }
        Assert.True(result.IsError != true, tool + ": " + WriteHost.Text(result));
        return data;
    }

    public async Task<JsonElement> ApplyErrorAsync(string id)
    {
        _safeToClean = false;
        CallToolResult call = await RawAsync(PlanApplyTools.ApplyTool, new() { ["plan_id"] = id });
        JsonElement data = call.StructuredContent!.Value;
        _safeToClean = data.TryGetProperty("execution", out JsonElement execution) && execution.GetString() != "unknown";
        Assert.True(call.IsError, WriteHost.Text(call));
        return data;
    }

    public async Task<JsonElement> WaitAsync(string taskId)
    {
        var watch = Stopwatch.StartNew();
        JsonElement task;
        do
        {
            task = await CallAsync(PlanApplyTools.TaskTool, new() { ["task_id"] = taskId, ["wait_seconds"] = 110 });
            Assert.True(watch.Elapsed < TimeSpan.FromMinutes(16), "The plan task exceeded its total budget.");
        }
        while (task.GetProperty("completedAt").ValueKind == JsonValueKind.Null);
        _safeToClean = task.GetProperty("status").GetString() is not ("unknown" or "interrupted");
        return task;
    }

    public async Task<string> CompileAsync(params object[] steps) => (await CallAsync(WorkflowTools.PlanTool, new()
    {
        ["project"] = Project,
        ["steps"] = steps
    })).GetProperty("planId").GetString()!;

    public Task<JsonElement> CreatePageAsync(string page) => WriteAsync("eplan_page_create", new()
    {
        ["project"] = Project,
        ["pages"] = new[] { new { name = page, description = "P25 setup" } },
        ["language"] = "en_US"
    });

    public async Task<string> ChangeSelectionToOtherProjectAsync()
    {
        await AssertCurrentAsync(_fixture!);
        string other = await CreateProjectAsync("P25o_" + _suffix);
        await OpenClosedAsync(other);
        await ReachCreatedAsync(other);
        await AssertCurrentAsync(other);
        return other;
    }

    public async Task AssertCurrentAsync(string project)
    {
        try
        {
            RequireNavigation();
            OpenProject[] open = await ProjectsAsync();
            AssertKnownOpenSet(open);
            Assert.Contains(open, p => IsProject(p, Project));
            Assert.Equal(PlanSnapshotHashes.ProjectKey(project), PlanSnapshotHashes.ProjectKey(Current(open).Path));
        }
        catch
        {
            _navigationFailed = true;
            throw;
        }
    }

    private async Task ReachCreatedAsync(string? expected = null)
    {
        try
        {
            RequireNavigation();
            OpenProject[] open = await ProjectsAsync();
            AssertKnownOpenSet(open);
            Assert.Contains(open, p => IsCreated(p.Path));
            while (!IsCreated(Current(open).Path))
            {
                string current = Current(open).Path;
                Assert.Contains(_initial, p => IsProject(p, current));
                await CloseCurrentAsync(current);
                open = await ProjectsAsync();
                AssertKnownOpenSet(open);
            }
            if (expected is not null)
            {
                Assert.True(IsProject(Current(open), expected), "The first created project must be selected after closing the known copies.");
            }
        }
        catch
        {
            _navigationFailed = true;
            throw;
        }
    }

    public void BeginExternalWrite() => _safeToClean = false;
    public void ExternalWriteSettled() => _safeToClean = true;
    public List<System.Text.Json.Nodes.JsonObject> Audit() => WriteHost.AuditRecordsIn(_home);
    public string[] Backups() => Directory.Exists(Path.Combine(_home, "backups"))
        ? [.. Directory.GetFiles(Path.Combine(_home, "backups"), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)] : [];

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_safeToClean && !_navigationFailed)
            {
                if (_client is not null && _initial.Length > 0)
                {
                    await RestoreInitialAsync();
                }
                if (Directory.Exists(_folder))
                {
                    string resolved = ScratchPaths.Canonical(_folder);
                    Assert.True(ScratchPaths.IsInside(resolved, [LiveEplan.ScratchRoot()]));
                    Assert.Equal(Path.GetFullPath(_folder), resolved, ignoreCase: true);
                    Directory.Delete(resolved, recursive: true);
                }
            }
        }
        catch
        {
            _navigationFailed = true;
            throw;
        }
        finally
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
            }
            TestContext.Current.TestOutputHelper!.WriteLine($"Retained audit and backups: {_home}; scratch: {_folder}; retained for unsettled write or unsafe navigation: {!_safeToClean || _navigationFailed}");
        }
    }

    private async Task CaptureInitialAsync(string fixture)
    {
        try
        {
            _initial = await ProjectsAsync();
            _fixture = fixture;
            Assert.True(ScratchPaths.IsInside(fixture, [Path.Combine(LiveEplan.ScratchRoot(), "fixtures")]));
            Assert.Equal(LiveEplan.ProjectName, Path.GetFileName(fixture));
            Assert.True(IsProject(Current(_initial), fixture));
            Assert.All(_initial, p => Assert.True(IsProject(p, fixture)
                || IsProject(p, Path.Combine(WriteHost.ScratchFolder, WriteHost.WritableName))
                || IsProject(p, Path.Combine(WriteHost.OutsideFolder, "LargeOutside")), "Only the known Large fixture copies may be open."));
        }
        catch
        {
            _navigationFailed = true;
            throw;
        }
    }

    private async Task<string> CreateProjectAsync(string name)
    {
        RequireNavigation();
        Assert.Null(ImportTools.ProjectNameProblem(name));
        Assert.All(_initial, p => Assert.True(StringComparer.OrdinalIgnoreCase.Compare(name, p.Name) > 0));
        string destination = Path.Combine(_folder, name);
        string project = Path.Combine(destination, name);
        _created.Add(project);
        JsonElement created = await WriteAsync("eplan_project_create", new()
        {
            ["mode"] = "basic_project",
            ["file"] = "basic.zw9",
            ["name"] = name,
            ["destination"] = destination
        });
        Assert.Equal("committed", created.GetProperty("execution").GetString());
        Assert.False(created.GetProperty("project").GetProperty("opened").GetBoolean());
        Assert.Equal(project + ".elk", created.GetProperty("project").GetProperty("path").GetString());
        Assert.DoesNotContain(await ProjectsAsync(), p => IsProject(p, project));
        return project;
    }

    private async Task OpenClosedAsync(string project, bool restoringFixture = false)
    {
        try
        {
            RequireNavigation();
            Assert.True(IsCreated(project) || _closed.Any(p => SameProject(p, project)));
            OpenProject[] before = await ProjectsAsync();
            AssertKnownOpenSet(before);
            Assert.DoesNotContain(before, p => IsProject(p, project));
            OpenProject? current = before.Length == 0 ? null : Current(before);
            if (current is not null && !restoringFixture)
            {
                string name = IsCreated(project) ? Path.GetFileName(project) : _initial.Single(p => IsProject(p, project)).Name;
                Assert.True(StringComparer.OrdinalIgnoreCase.Compare(name, current.Name) > 0, "Only open projects sorting after the current project.");
            }
            // A46: only a later-sorting closed project has observed selection preservation; an empty navigator selects it.
            // Fixture restoration is checked explicitly, without inferring any rule for opening an earlier name.
            await PlanStepLiveSession.ActionAsync("ProjectOpen /Project:\"" + project + ".elk\" /Quiet");
            OpenProject[] after = await ProjectsAsync();
            Assert.Equal(Paths(before).Append(PlanSnapshotHashes.ProjectKey(project)).Order(StringComparer.Ordinal), Paths(after));
            string expected = current is null || restoringFixture ? project : current.Path;
            Assert.True(IsProject(Current(after), expected));
        }
        catch
        {
            _navigationFailed = true;
            throw;
        }
    }

    private async Task CloseCurrentAsync(string project)
    {
        try
        {
            RequireNavigation();
            bool knownCopy = _initial.Any(p => IsProject(p, project));
            Assert.True(IsCreated(project) || knownCopy, "Never close an unknown project.");
            OpenProject[] before = await ProjectsAsync();
            AssertKnownOpenSet(before);
            Assert.True(IsProject(Current(before), project), "The intended project must be current immediately before closing.");
            await PlanStepLiveSession.ActionAsync("XPrjActionProjectClose");
            OpenProject[] after = await ProjectsAsync();
            Assert.Equal(Paths(before.Where(p => !IsProject(p, project))), Paths(after));
            if (knownCopy)
            {
                _closed.Add(project);
            }
        }
        catch
        {
            _navigationFailed = true;
            throw;
        }
    }

    public async Task RestoreInitialAsync()
    {
        try
        {
            RequireNavigation();
            OpenProject[] open = await ProjectsAsync();
            AssertKnownOpenSet(open);
            if (open.Any(p => IsCreated(p.Path)))
            {
                // Ordinary cases preserve selection until cleanup; close only known current copies to reach our projects.
                await ReachCreatedAsync();
                open = await ProjectsAsync();
            }
            while (open.Any(p => IsCreated(p.Path)))
            {
                string current = Current(open).Path;
                Assert.True(IsCreated(current), "Stop cleanup if an initial or unknown project would have to be closed.");
                await CloseCurrentAsync(current);
                open = await ProjectsAsync();
            }
            // Do not reopen an earlier-sorting copy while a created project remains open. Restore the fixture first.
            OpenProject[] closed = [.. _initial.Where(p => _closed.Any(c => IsProject(p, c)))
                .OrderBy(p => IsProject(p, _fixture!) ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
            foreach (OpenProject copy in closed)
            {
                await OpenClosedAsync(copy.Path, restoringFixture: IsProject(copy, _fixture!));
                _closed.RemoveAll(p => IsProject(copy, p));
                Assert.True(IsProject(Current(await ProjectsAsync()), _fixture!), "Stop if reopening the known copies does not restore fixture selection.");
            }
            open = await ProjectsAsync();
            Assert.Equal(Paths(_initial), Paths(open));
            Assert.True(IsProject(Current(open), _fixture!));
        }
        catch
        {
            _navigationFailed = true;
            throw;
        }
    }

    private void RequireNavigation()
    {
        Assert.True(_safeToClean && !_navigationFailed, "Retain all projects while a write is unsettled or navigation is unsafe.");
    }

    private void AssertKnownOpenSet(OpenProject[] open)
    {
        Assert.All(open, p => Assert.True(IsCreated(p.Path) || _initial.Any(i => SameProject(i.Path, p.Path)), "An unknown project appeared; stop navigation."));
        Assert.All(_initial.Where(p => !_closed.Any(c => IsProject(p, c))), p => Assert.Contains(open, o => SameProject(o.Path, p.Path)));
        Assert.All(_closed, p => Assert.DoesNotContain(open, o => IsProject(o, p)));
    }

    private Task<CallToolResult> RawAsync(string tool, Dictionary<string, object?> args) => _client!.CallToolAsync(tool, args, cancellationToken: Token).AsTask();
    private async Task<OpenProject[]> ProjectsAsync() => JsonSerializer.Deserialize<OpenProject[]>(
        (await CallAsync("eplan_project_list", new() { ["limit"] = 200 })).GetProperty("projects").GetRawText(), HostJson.Options)!;
    private bool IsCreated(string path) => _created.Any(p => SameProject(p, path));
    private static OpenProject Current(OpenProject[] open) => Assert.Single(open, p => p.Current);
    private static string[] Paths(IEnumerable<OpenProject> open) => [.. open.Select(p => PlanSnapshotHashes.ProjectKey(p.Path)).Order(StringComparer.Ordinal)];
    private static bool SameProject(string left, string right) => PlanSnapshotHashes.ProjectKey(left) == PlanSnapshotHashes.ProjectKey(right);
    private static bool IsProject(OpenProject entry, string path) => SameProject(entry.Path, path);
}
