using System.Globalization;
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
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Large.Tests;

/// <summary>S8 U1 only: an attended Host wait timeout, without cancelling work or terminating EPLAN.</summary>
[Trait("Size", "Large")]
public sealed class PlanSettlementTests(LiveEplan eplan)
{
    internal const string AttendVariable = "EPLANMCP_LARGE_PLAN_TIMEOUT";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task P21_ControlledHostTimeout_FreshSettlement_MatchesCommittedPages()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(AttendVariable) == "1",
            "S8 safety: an engineer must attend, open only the scratch LargeWrite copy, and set EPLANMCP_LARGE_PLAN_TIMEOUT=1.");
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        ProjectListData opened = await bridge.InvokeAsync<object, ProjectListData>(OperationNames.ProjectList, new { }, Token);
        OpenProject only = Assert.Single(opened.Projects);
        string project = only.Path;
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(project));
        Assert.True(ScratchPaths.IsInside(project, [WriteHost.ScratchFolder]));
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new() { Project = project });
        int first = before.Pages.Where(p => p.Name.StartsWith(DestructiveHost.Prefix, StringComparison.Ordinal))
            .Select(p => DestructiveHost.Number(p.Name)).DefaultIfEmpty().Max() + 100;
        string[] names = [.. Enumerable.Range(first, 50).Select(n => DestructiveHost.Prefix + n.ToString(CultureInfo.InvariantCulture))];
        var locations = new HostDataLocations(Directory.CreateTempSubdirectory("eplanmcp-p21-large-").FullName);
        HostConfig config = HostConfig.Defaults(locations) with { ScratchRoots = [WriteHost.ScratchFolder] };
        var audit = new AuditLog(locations.Audit, TimeProvider.System);
        var store = new PlanStore(locations, audit);
        var packs = new PackLibrary(locations, config, audit, TimeProvider.System);
        var trust = new PlanTrust();
        var folders = new BackupFolders(locations, TimeProvider.System);
        var suspensions = SuspensionStore.For(locations);
        using var oldTasks = new TaskRegistry(TimeProvider.System);
        using var newTasks = new TaskRegistry(TimeProvider.System);
#pragma warning disable CA2000 // The server owns its transport.
        await using McpServer server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        {
            ServerInfo = new() { Name = "P21 attended timeout", Version = "1" },
        });
#pragma warning restore CA2000
        var orchestrator = new PlanOrchestrator(bridge, packs, store, trust, oldTasks, suspensions,
            new(bridge, suspensions, audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new([], config, TimeProvider.System, NullLogger<ApprovalService>.Instance), audit, folders,
            new(bridge, folders, TimeProvider.System, NullLogger<PlanProjectBackup>.Instance));
        Task<WriteResult<JsonElement>>? inFlight = null;
        try
        {
            JsonElement inputs = JsonSerializer.SerializeToElement(new object[]
            {
                new { op = "page.create", args = new { pages = names.Select(name => new { name, page_type = "Circuit", description = "P21 created" }), language = "en_US" } },
                new { op = "page.update", args = new { pages = new[] { new { name = new { from_step = 1, page = names[0] }, description = "P21 must remain unsent" } }, language = "en_US" } },
            });
            CallToolResult compiled = await WorkflowTools.PlanAsync(bridge, packs, store, trust, oldTasks, server, project, inputs, cancellationToken: Token);
            Assert.False(compiled.IsError == true, string.Join("\n", compiled.Content.OfType<TextContentBlock>().Select(c => c.Text)));
            string id = compiled.StructuredContent!.Value.GetProperty("planId").GetString()!;
            using PlanPreparation preparation = await orchestrator.PrepareAsync(server, id, false, null, Token);
            Assert.Equal(PlanStatus.Running, preparation.Record.State.Status);
            PlanDefinition definition = preparation.Record.Definition;
            PlanStep step = definition.Steps[0];
            byte[] bytes = await store.ReadEvidenceAsync(id, definition.ContextRef!["sha256:".Length..], Token);
            var baseline = new PlanEvidence(JsonSerializer.Deserialize<PlanBaseline>(bytes, PlanJson.Options)!.Snapshot, true);
            WritePlan fresh = await new PlanStepCompiler(bridge).PlanAsync(project, new(step.Index, step.Op, step.Args, step.Tier, step.DependsOn), Token);
            PlanStepExecuteArgs args = PlanStepRequest.Build(definition, step, fresh, [], baseline, null);
            string requestId = Guid.NewGuid().ToString("N");
            JsonObject exited = DanglingWrites.ThisHost();
            exited["start"] = DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture);
            // Model only the old Host's exit. The real EPLAN PID/start and the request ID are never substituted.
            await audit.AppendAsync(new JsonObject
            {
                ["kind"] = "plan_approval",
                ["planId"] = id,
                ["ownerHost"] = preparation.Record.State.OwnerHost,
                ["host"] = exited.DeepClone(),
            }, Token);
            await audit.AppendAsync(new JsonObject
            {
                ["kind"] = "execution_started",
                ["planId"] = id,
                ["step"] = step.Index,
                ["requestId"] = requestId,
                ["project"] = project,
                ["tool"] = "eplan_plan_apply",
                ["host"] = exited,
                ["eplan"] = new JsonObject
                {
                    ["processId"] = eplan.ProcessId,
                    ["start"] = preparation.InstanceStart!.Value.ToString("O", CultureInfo.InvariantCulture),
                },
                ["changes"] = JsonSerializer.SerializeToNode(WriteChangeLines.For(fresh)),
            }, Token);

            // The Host's 1 ms wait expires, while the transport keeps observing the bounded native write.
            // No cancellation reaches EPLAN and no execution_result is written by the simulated exited owner.
            inFlight = bridge.InvokeWriteAsync<PlanStepExecuteArgs, JsonElement>(OperationNames.PlanStepExecute,
                args, requestId, TimeSpan.FromSeconds(120), null, Token);
            await Assert.ThrowsAsync<TimeoutException>(async () => await inFlight.WaitAsync(TimeSpan.FromMilliseconds(1), Token));
            Assert.False(inFlight.IsCompleted, "The controlled Host timeout must happen before the step completes.");
            _ = await inFlight;
            preparation.Dispose();
            Assert.Equal(PlanStatus.Running, (await store.ReadAsync(id, Token))!.State.Status);

            // A fresh Host has no old tasks or trust; settlement asks only outcome.get and never sends step 2.
            Assert.NotEqual(oldTasks.OwnerFor(server).RunId, newTasks.OwnerFor(server).RunId);
            var restartedStore = new PlanStore(locations, new AuditLog(locations.Audit, TimeProvider.System));
            await using BridgeClient restartedBridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            var settlement = new PlanSettlement(restartedStore, audit, locations, TimeProvider.System,
                new(restartedBridge, suspensions, audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance));
            await settlement.StartAsync(Token);
            PlanRecord terminal = (await restartedStore.ReadAsync(id, Token))!;
            Assert.Equal(PlanStatus.PartiallyCommitted, terminal.State.Status);
            Assert.Equal(PlanExecution.PartiallyCommitted, terminal.State.Result!.Execution);
            Assert.Equal(ExecutionState.Committed, terminal.State.Result.Steps[0].Execution);
            Assert.True(terminal.State.Result.Steps[0].Recovered);
            Assert.Equal(ExecutionState.NotStarted, terminal.State.Result.Steps[1].Execution);
            Assert.Equal("skipped", terminal.State.Result.Verification);
            Assert.Equal(preparation.Backup, Assert.Single(terminal.State.Result.Backups));
            Assert.False(new PlanTrust().Owns(id, newTasks.OwnerFor(server)));
            Assert.Throws<TaskRegistryException>(() => newTasks.Get(server, terminal.State.TaskId!));
            LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(restartedBridge, new() { Project = project });
            Assert.Equal(before.Pages.Count + names.Length, after.Pages.Count);
            Assert.All(names, name => Assert.Single(after.Pages, p => p.Name == name));
            PageGetData page = await restartedBridge.InvokeAsync<PageGetArgs, PageGetData>(OperationNames.PageGet,
                new() { Project = project, Page = names[0], Language = "en_US" }, Token);
            Assert.Equal("P21 created", page.Page.Description);
            await settlement.SettleAsync(Token);
            Assert.Equal(JsonSerializer.Serialize(terminal.State, PlanJson.Options),
                JsonSerializer.Serialize((await restartedStore.ReadAsync(id, Token))!.State, PlanJson.Options));
            Assert.Empty(suspensions.All());
            Assert.Null(AuditVerifier.Verify(locations.Audit).Break);
            TestContext.Current.TestOutputHelper!.WriteLine($"Host timeout: 1 ms; actual committed pages: {names.Length}; terminal: {terminal.State.Status}; retained evidence and backup: {locations.Root}");
        }
        finally
        {
            // Do not race cleanup against native work, including when an earlier assertion failed.
            bool settled = true;
            if (inFlight is not null)
            {
                try
                {
                    _ = await inFlight;
                }
                catch (BridgeCallException error)
                {
                    settled = error.Error.Execution != ExecutionState.Unknown;
                    // Unknown native work must remain untouched for the attending engineer's inspection.
                }
            }
            if (settled)
            {
                LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, new() { Project = project });
                string[] created = [.. names.Where(name => after.Pages.Any(p => p.Name == name))];
                if (created.Length > 0)
                {
                    var remove = new PageDeleteArgs { Project = project, Pages = [.. created] };
                    remove.Fingerprint = (await bridge.InvokeAsync<PageDeleteArgs, WritePlan>(OperationNames.PageDeletePlan, remove, Token)).Fingerprint;
                    remove.Backup = new() { Path = folders.Create(project), MaxSeconds = 60 };
                    await bridge.InvokeWriteAsync<PageDeleteArgs, JsonElement>(OperationNames.PageDeleteExecute, remove,
                        Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(120), null, Token);
                }
            }
        }
    }
}
