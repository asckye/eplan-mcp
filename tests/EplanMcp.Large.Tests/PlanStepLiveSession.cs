using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Large.Tests;

/// <summary>Scratch-only setup, direct Bridge writes and current-project undo, following S35/A41.</summary>
internal sealed class PlanStepLiveSession(BridgeClient bridge, string project, List<OpenProject> original) : IAsyncDisposable
{
    private readonly List<string> _closed = [];
    private readonly HashSet<string> _originalPages = new(StringComparer.Ordinal);
    private readonly string _backups = Directory.CreateTempSubdirectory("eplanmcp-p17-backups-").FullName;
    private int _nextNumber;
    public string Project => project;
    public int NextNumber => ++_nextNumber;
    public string NextPage => DestructiveHost.Prefix + NextNumber.ToString(CultureInfo.InvariantCulture);

    public static async Task<PlanStepLiveSession> StartAsync(LiveEplan eplan)
    {
        string project = await eplan.WriteProjectAsync();
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(project));
#pragma warning disable CA2000 // Ownership transfers to the session, which disposes on setup failure and at test completion.

        BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
#pragma warning restore CA2000
        List<OpenProject> projects = (await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), TestContext.Current.CancellationToken)).Projects;
        var run = new PlanStepLiveSession(bridge, project, projects);
        try
        {
            // XEsUndoAction has no target parameter: close only known fixture copies, never an engineer's project.
            while (!string.Equals((await run.CurrentAsync()).Path, project, StringComparison.OrdinalIgnoreCase))
            {
                OpenProject current = await run.CurrentAsync();
                Assert.True(current.Path.StartsWith(Path.Combine(LiveEplan.ScratchRoot(), "fixtures") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(current.Path, Path.Combine(WriteHost.OutsideFolder, "LargeOutside"), StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(current.Path, run._closed);
                await ActionAsync("XPrjActionProjectClose");
                run._closed.Add(current.Path);
            }

            await run.AssertCurrentAsync();
            LiveSnapshots.Snapshot snapshot = await run.SnapshotAsync();
            run._originalPages.UnionWith(snapshot.Pages.Select(p => p.Name));
            run._nextNumber = snapshot.Pages.Where(p => p.Name.StartsWith(DestructiveHost.Prefix, StringComparison.Ordinal)).Select(p => DestructiveHost.Number(p.Name)).DefaultIfEmpty().Max() + 100;
            ProjectBackupData backup = await bridge.InvokeAsync<ProjectBackupArgs, ProjectBackupData>(OperationNames.ProjectBackup, new() { Project = project, Target = run.Target() }, TimeSpan.FromSeconds(75), null, TestContext.Current.CancellationToken);
            Assert.Equal(snapshot.Pages.Count, backup.Pages);
            await run.AssertCurrentAsync();
            return run;
        }
        catch
        {
            await run.DisposeAsync();
            throw;
        }
    }

    public async Task<string[]> CreatePagesAsync(int count)
    {
        string[] names = [.. Enumerable.Range(0, count).Select(_ => NextPage)];
        var args = new PageCreateArgs
        {
            Project = Project,
            Language = "en_US",
            Pages = [.. names.Select(name => new PageCreateItem { Name = name, Description = "P17 setup" })]
        };
        args.Fingerprint = (await PlanAsync("page.create", args)).Fingerprint;
        await bridge.InvokeWriteAsync<PageCreateArgs, PageCreateData>(OperationNames.PageCreateExecute, args, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(60), null, TestContext.Current.CancellationToken);
        return names;
    }

    public async Task<PlanStepExecuteArgs> PrepareAsync(string op, object args, string[] pages)
    {
        WritePlan plan = await PlanAsync(op, args);
        LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(bridge, new() { Project = Project, Pages = [.. pages] });
        SnapshotObject[] objects = [.. snapshot.Pages.SelectMany(p => p.Objects)];
        var scope = new PlanGuardScope
        {
            Pages = [.. pages]
        };
        var step = new PlanStepExecuteArgs
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Index = 1,
            Total = 1,
            Fingerprint = plan.Fingerprint,
            ApprovedChanges = plan.Changes,
            ApprovedDetails = plan.Details,
            Guard = new()
            {
                Project = Project,
                ReadScope = scope,
                Preconditions = [new()
                {
                    Tag = "members",
                    Scope = scope,
                    Count = objects.Length,
                    Hash = PlanSnapshotHashes.Members(Project, objects.Select(o => new KeyValuePair<string, string>(PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o.Hash))),
                }
                ],
            },
        };
        switch (args)
        {
            case PageCreateArgs a:
                step.PageCreate = a;
                step.Guard.Preconditions.AddRange(a.Pages.Select(p => new PlanGuardCondition { Tag = "absent", Kind = "page", Name = p.Name }));
                break;
            case PageUpdateArgs a:
                step.PageUpdate = a;
                break;
            case PageDeleteArgs a:
                step.PageDelete = a;
                step.Backup = Target();
                break;
            case PageRenumberArgs a:
                step.PageRenumber = a;
                step.Backup = Target();
                break;
            case DeviceUpdateArgs a:
                step.DeviceUpdate = a;
                break;
            case DeviceRenumberArgs a:
                step.DeviceRenumber = a;
                step.Backup = Target();
                break;
            case MacroInsertArgs a:
                step.MacroInsert = a;
                step.MacroConstraint = new()
                {
                    Arguments = a,
                    Description = await InspectAsync(a.Macro!)
                };
                break;
        }

        return step;
    }

    public Task<WriteResult<JsonElement>> ExecuteAsync(PlanStepExecuteArgs step, string request) => bridge.InvokeWriteAsync<PlanStepExecuteArgs, JsonElement>(OperationNames.PlanStepExecute, step, request, TimeSpan.FromSeconds(120), null, TestContext.Current.CancellationToken);
    public async Task<string> OutcomeAsync(string request) => (await bridge.InvokeAsync<OutcomeGetArgs, OutcomeGetData>(OperationNames.OutcomeGet, new() { RequestId = request }, TestContext.Current.CancellationToken)).Status;
    public Task<LiveSnapshots.Snapshot> SnapshotAsync(params string[] pages) => LiveSnapshots.TakeAsync(bridge, new() { Project = Project, Pages = pages.Length == 0 ? null : [.. pages] });
    public async Task<string> StateAsync() => string.Join("\n", (await SnapshotAsync()).Pages.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "\t" + p.Hash));
    public async Task<string[]> OpenPathsAsync() => [.. (await ProjectsAsync()).Select(p => p.Path).Order(StringComparer.Ordinal)];
    public async Task UndoAsync()
    {
        await AssertCurrentAsync();
        await ActionAsync("XEsUndoAction");
        await AssertCurrentAsync();
    }

    public async Task UpdateAsync(string page, string text)
    {
        var args = new PageUpdateArgs
        {
            Project = Project,
            Language = "en_US",
            Pages = [new()
            {
                Name = page,
                Description = text
            }
            ]
        };
        args.Fingerprint = (await PlanAsync("page.update", args)).Fingerprint;
        await bridge.InvokeWriteAsync<PageUpdateArgs, PageUpdateData>(OperationNames.PageUpdateExecute, args, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(60), null, TestContext.Current.CancellationToken);
    }

    public async Task<JsonElement> InsertFixtureMacroAsync(string page)
    {
        var args = new MacroInsertArgs
        {
            Project = Project,
            Page = page,
            Macro = MacroTests.FeedbackControl,
            X = 40,
            Y = 280,
            Numbering = "number"
        };
        args.Fingerprint = (await PlanAsync("macro.insert", args)).Fingerprint;
        WriteResult<JsonElement> result = await bridge.InvokeWriteAsync<MacroInsertArgs, JsonElement>(OperationNames.MacroInsertExecute, args, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(60), null, TestContext.Current.CancellationToken);
        Assert.False(result.Recovered);
        return result.Data;
    }

    public async Task RenameAsync(string tag, string newTag)
    {
        var args = new DeviceUpdateArgs
        {
            Project = Project,
            Tag = tag,
            NewTag = newTag
        };
        args.Fingerprint = (await PlanAsync("device.update", args)).Fingerprint;
        await bridge.InvokeWriteAsync<DeviceUpdateArgs, DeviceUpdateData>(OperationNames.DeviceUpdateExecute, args, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(60), null, TestContext.Current.CancellationToken);
    }

    public async Task<string> FindSupportedMacroAsync(string page)
    {
        ProjectSnapshotData head = (await SnapshotAsync()).Chunks[0];
        PlanBaselineRead read = await new PlanBaselineReader(bridge).ReadAsync(new(head.Project, head.ProjectPath, head.ProjectGuid), null, TestContext.Current.CancellationToken);
        Assert.Null(read.Reason);
        PlanBaseline baseline = Assert.IsType<PlanBaseline>(read.Baseline);
        int offset = 0;
        int total;
        do
        {
            MacroListData list = await bridge.InvokeAsync<MacroListArgs, MacroListData>(OperationNames.MacroList, new() { Limit = 200, Offset = offset }, TestContext.Current.CancellationToken);
            Assert.False(list.Incomplete);
            total = list.Total;
            foreach (MacroListItem macro in list.Items)
            {
                PlanMacroInspectData description;
                try
                {
                    description = await InspectAsync(macro.Path);
                }
                catch (BridgeCallException ex) when (ex.Error.Reason is ErrorReason.CapabilityMissing or ErrorReason.InvalidArgument)
                {
                    continue;
                }

                if (description.Functions.Count > 0 && description.Frame.Right <= 380 && description.Frame.Bottom >= -280 && !description.Functions.Any(f => baseline.Names.Where(n => n.Kind == "function").Any(o => PlanNamePattern.Matches(PlanNamePattern.For(page, f), o.Name) || PlanNamePattern.Matches(@"\A" + PlanNamePattern.Label(f.Name.Split('-')[^1].Split(':')[0]) + @"\z", o.Name.Split('-')[^1].Split(':')[0]))))
                {
                    return macro.Path;
                }
            }

            Assert.True(list.Items.Count > 0 || offset >= total);
            offset += list.Items.Count;
        }
        while (offset < total);
        Assert.Skip("The fixture has no complete, conflict-free MultiLine variant 0 macro fitting an empty Circuit page.");
        return "";
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_originalPages.Count > 0)
            {
                string[] added = [.. (await SnapshotAsync()).Pages.Select(p => p.Name).Where(p => !_originalPages.Contains(p))];
                if (added.Length > 0)
                {
                    PlanStepExecuteArgs cleanup = await PrepareAsync("page.delete", new PageDeleteArgs { Project = Project, Pages = [.. added] }, added);
                    await ExecuteAsync(cleanup, Guid.NewGuid().ToString("N"));
                }
            }
        }
        finally
        {
            try
            {
                // Restore the fixture first (A41: opening another project when only LargeWrite remains selects it).
                foreach (string path in _closed.OrderBy(p => original.Single(o => o.Path == p).Current ? 0 : 1))
                {
                    await ActionAsync("ProjectOpen /Project:\"" + path + ".elk\" /Quiet");
                }

                Assert.Equal(original.Select(p => p.Path).Order(StringComparer.Ordinal), await OpenPathsAsync());
                Assert.Equal(original.Single(p => p.Current).Path, (await CurrentAsync()).Path, ignoreCase: true);
            }
            finally
            {
                await bridge.DisposeAsync();
            }
        }
    }

    private Task<WritePlan> PlanAsync(string op, object args) => bridge.InvokeAsync<object, WritePlan>(op + ".plan", args, TestContext.Current.CancellationToken);
    private Task<PlanMacroInspectData> InspectAsync(string macro) => bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect, new() { Macro = macro }, TestContext.Current.CancellationToken);
    private async Task<List<OpenProject>> ProjectsAsync() => (await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), TestContext.Current.CancellationToken)).Projects;
    private async Task<OpenProject> CurrentAsync() => Assert.Single(await ProjectsAsync(), p => p.Current);
    private async Task AssertCurrentAsync() => Assert.Equal(Project, (await CurrentAsync()).Path, ignoreCase: true);
    private BackupTarget Target() => new()
    {
        Path = Path.Combine(Directory.CreateDirectory(Path.Combine(_backups, Guid.NewGuid().ToString("N"))).FullName, "LargeWrite.elk"),
        MaxSeconds = 60,
    };
    internal static async Task ActionAsync(string action)
    {
        string remote = Environment.GetEnvironmentVariable("EPLANMCP_EPLREMOTE") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".local", "tools", "eplremote", "bin", "Release", "net481", "eplremote.exe"));
        var start = new ProcessStartInfo(remote)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("EPLREMOTE_HOST") ?? throw new InvalidOperationException("Set EPLREMOTE_HOST to this machine's IPv4 address (S35/A41)."));
        start.ArgumentList.Add(action);
        start.Environment["EPLREMOTE_PORT"] = Environment.GetEnvironmentVariable("EPLREMOTE_PORT") ?? "49152";
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
        TestContext.Current.TestOutputHelper!.WriteLine($"eplremote {action}: {await output} {await error}");
    }
}
