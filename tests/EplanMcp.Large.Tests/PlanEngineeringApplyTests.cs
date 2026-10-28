using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Large.Tests;

/// <summary>W3: seven existing operations on LargeWrite, with compiler 3, retained backups and real outcomes.</summary>
[Trait("Size", "Large")]
public sealed class PlanEngineeringApplyTests(LiveEplan eplan)
{
    private static readonly string[] _evidenceWindows = ["1:before", "1:after"];
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("page.create", false)]
    [InlineData("page.update", false)]
    [InlineData("page.delete", false)]
    [InlineData("page.renumber", false)]
    [InlineData("device.update", false)]
    [InlineData("device.renumber", false)]
    [InlineData("macro.insert", false)]
    [InlineData("page.update", true)]
    public async Task Compiler3_SevenOperations_BackupOutcomeAndOneUndoRemainValid(string op, bool recover)
    {
        await using PlanStepLiveSession run = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(run.Project));
        Assert.True(ScratchPaths.IsInside(run.Project, [WriteHost.ScratchFolder]));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        string target = (await run.CreatePagesAsync(2))[1];
        object input;
        switch (op)
        {
            case "page.create":
                input = new { pages = new[] { new { name = run.NextPage, description = "W3 created" } }, language = "en_US" };
                break;
            case "page.update":
                input = new { pages = new[] { new { name = target, description = "W3 updated" } }, language = "en_US" };
                break;
            case "page.delete":
                input = new { pages = new[] { target } };
                break;
            case "page.renumber":
                input = new { pages = new[] { target }, start = run.NextNumber };
                break;
            case "device.update":
            case "device.renumber":
                JsonElement inserted = await run.InsertFixtureMacroAsync(target);
                string tag = inserted.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!)
                    .First(t => t.Contains('-', StringComparison.Ordinal) && !t.Contains('?', StringComparison.Ordinal) && !t.Contains(':', StringComparison.Ordinal));
                if (op == "device.update")
                { input = new { tag, new_tag = tag + "99" }; }
                else
                {
                    await run.RenameAsync(tag, tag.Insert(tag.LastIndexOf('-') + 1, "?") + "99");
                    input = new { pages = new[] { target }, scheme = "Standard", only_unnumbered = true, start = run.NextNumber };
                }
                break;
            default:
                input = new { page = target, macro = await run.FindSupportedMacroAsync(target), x = 40, y = 280, numbering = "keep" };
                break;
        }

        LiveSnapshots.Snapshot before = await run.SnapshotAsync();
        var locations = new HostDataLocations(Directory.CreateTempSubdirectory("eplanmcp-w3-large-").FullName);
        HostConfig config = HostConfig.Defaults(locations) with { ScratchRoots = [WriteHost.ScratchFolder] };
        var audit = new AuditLog(locations.Audit, TimeProvider.System);
        var packs = new PackLibrary(locations, config, audit, TimeProvider.System);
        var store = new PlanStore(locations, audit);
        var trust = new PlanTrust();
        using var tasks = new TaskRegistry(TimeProvider.System);
#pragma warning disable CA2000 // McpServer owns and disposes the test transport.
        await using McpServer server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        { ServerInfo = new() { Name = "W3 Large", Version = "1" } });
#pragma warning restore CA2000
        string workspace = packs.Workspaces.FolderOf(run.Project);
        Directory.CreateDirectory(workspace);
        await File.WriteAllBytesAsync(Path.Combine(workspace, ProjectBinding.FileName), PackJson.Serialize(new ProjectBinding
        {
            BindingVersion = 2,
            Project = PlanSnapshotHashes.ProjectKey(run.Project),
            Revision = 1,
            PackRoots = [],
            ProjectValues = new(),
            Cabinets = [new() { Id = "default", Layers = [], Values = new(), Structure = new JsonObject { ["all"] = true } }],
        }), Token);
        var client = new OutcomeBridge(bridge, recover);
        var channel = new ScriptedApprovalChannel();
        var approvals = new ApprovalService([channel], config, TimeProvider.System, NullLogger<ApprovalService>.Instance);
        var reviews = new KnowledgeReviewService(packs, audit, approvals, client);
        var folders = new BackupFolders(locations, TimeProvider.System);
        var suspensions = SuspensionStore.For(locations);
        var orchestrator = new PlanOrchestrator(client, packs, store, trust, tasks, suspensions,
            new(client, suspensions, audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance), approvals, audit, folders,
            new(client, folders, TimeProvider.System, NullLogger<PlanProjectBackup>.Instance));
        CallToolResult compiled = await WorkflowTools.PlanAsync(client, packs, store, trust, tasks, server, run.Project,
            JsonSerializer.SerializeToElement(new[] { new { op, args = input } }), reviews: reviews, cancellationToken: Token);
        Assert.False(compiled.IsError == true, WriteHost.Text(compiled));
        string id = compiled.StructuredContent!.Value.GetProperty("planId").GetString()!;
        PlanRecord plan = (await store.ReadAsync(id, Token))!;
        Assert.Equal((2, "3"), (plan.SchemaVersion, plan.Definition.CompilerVersion));
        Assert.Empty(plan.Definition.Engineering!.ConfigValuesAndTrace);
        channel.Approve("eplan_plan_apply", run.Project, []);
        using PlanPreparation prepared = await orchestrator.PrepareAsync(server, id, false, null, Token);
        Assert.Equal(PlanStatus.Running, prepared.Record.State.Status);
        PlanResult result = (await orchestrator.ExecuteAsync(server, prepared, null, Token)).State.Result!;
        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal(recover, Assert.Single(result.Steps).Recovered);
        Assert.Single(client.Requests);
        Assert.Equal(OutcomeStatus.Committed, await run.OutcomeAsync(client.Requests[0]));
        Assert.Equal(plan.Definition.Steps[0].Tier == ToolTier.Destructive ? 2 : 1, result.Backups.Length);
        Assert.All(result.Backups, b => Assert.True(b.Usable && File.Exists(b.Path)));
        LiveSnapshots.Snapshot backup = await LiveSnapshots.TakeAsync(bridge, new() { Project = result.Backups[0].Path, OpenReadOnly = true });
        Assert.Equal(before.Pages.Select(p => (p.Name, p.Hash)).Order(), backup.Pages.Select(p => (p.Name, p.Hash)).Order());
        foreach (string window in _evidenceWindows)
        {
            byte[] bytes = await store.ReadEvidenceAsync(id, result.Coverage!.Value.GetProperty("evidence").GetProperty(window).GetString()!, Token);
            using JsonDocument evidence = JsonDocument.Parse(bytes);
            Assert.False(evidence.RootElement.GetProperty("wholeProject").GetBoolean());
        }
        string actual = await run.StateAsync();
        string expected = string.Join("\n", before.Pages.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "\t" + p.Hash));
        if (op == "device.renumber" && actual == expected)
        { Assert.Skip("EPLAN left fixture tags unchanged (A41); no undo is safe for an ineffective step."); }
        Assert.NotEqual(expected, actual);
        await run.UndoAsync();
        Assert.Equal(expected, await run.StateAsync());
        Assert.Equal(OutcomeStatus.Committed, await run.OutcomeAsync(client.Requests[0]));
        TestContext.Current.TestOutputHelper!.WriteLine($"{op}; recovered={recover}; retained evidence and backups: {locations.Root}");
    }

    /// <summary>Simulates a lost reply only after real outcome.get proves the original request committed; never resends.</summary>
    private sealed class OutcomeBridge(IBridgeClient inner, bool recover) : IBridgeClient
    {
        public List<string> Requests { get; } = [];
        public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => inner.GetStatusAsync(cancellationToken);
        public Task<BridgeStatus> GetStatusAsync(bool refresh, CancellationToken cancellationToken) => inner.GetStatusAsync(refresh, cancellationToken);
        public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => inner.SelectAsync(processId, cancellationToken);
        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken) => inner.InvokeAsync<TArgs, TData>(operation, args, cancellationToken);
        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) =>
            inner.InvokeAsync<TArgs, TData>(operation, args, timeout, progress, cancellationToken);
        public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => inner.FetchOutcomeAsync(processId, requestId, cancellationToken);
        public async Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
        {
            Assert.Equal(OperationNames.PlanStepExecute, operation);
            Requests.Add(requestId);
            WriteResult<TData> result = await inner.InvokeWriteAsync<TArgs, TData>(operation, args, requestId, limit, progress, cancellationToken);
            OutcomeGetData outcome = await inner.InvokeAsync<OutcomeGetArgs, OutcomeGetData>(OperationNames.OutcomeGet, new() { RequestId = requestId }, cancellationToken);
            Assert.Equal((OutcomeStatus.Committed, operation), (outcome.Status, outcome.Operation));
            return recover ? new(default, true, ExecutionState.Committed) : result;
        }
    }
}
