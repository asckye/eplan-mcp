using System.Globalization;
using System.Text.Json;
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

/// <summary>Direct P20 orchestration on LargeWrite only; backups are retained and opened read-only (A34/A41).</summary>
[Trait("Size", "Large")]
public sealed class PlanOrchestratorTests(LiveEplan eplan)
{
    [Fact]
    public Task P20_TwoStepPlan_BothCommitted_VerificationPassed_InitialBackupOpens() => RunAsync(conflict: false);

    [Fact]
    public Task P20_SecondStepConflict_PartiallyCommitted_InitialBackupOpens() => RunAsync(conflict: true);

    private async Task RunAsync(bool conflict)
    {
        string project = await eplan.WriteProjectAsync();
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(project));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new() { Project = project });
        int next = before.Pages.Where(p => p.Name.StartsWith(DestructiveHost.Prefix, StringComparison.Ordinal))
            .Select(p => DestructiveHost.Number(p.Name)).DefaultIfEmpty().Max() + 100;
        string page = DestructiveHost.Prefix + next.ToString(CultureInfo.InvariantCulture);
        var locations = new HostDataLocations(Directory.CreateTempSubdirectory("eplanmcp-p20-large-").FullName);
        HostConfig config = HostConfig.Defaults(locations) with { ScratchRoots = [WriteHost.ScratchFolder] };
        var audit = new AuditLog(locations.Audit, TimeProvider.System);
        var packs = new PackLibrary(locations, config, audit, TimeProvider.System);
        var store = new PlanStore(locations, audit);
        var trust = new PlanTrust();
        using var tasks = new TaskRegistry(TimeProvider.System);
#pragma warning disable CA2000 // McpServer owns the transport and closes it with this test session.
        await using McpServer server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        {
            ServerInfo = new() { Name = "P20 Large", Version = "1" },
        });
#pragma warning restore CA2000
        var suspensions = SuspensionStore.For(locations);
        var folders = new BackupFolders(locations, TimeProvider.System);
        var client = new InterveningBridge(bridge, conflict ? () => UpdateAsync(bridge, project, page, "Changed after compilation") : null);
        var orchestrator = new PlanOrchestrator(client, packs, store, trust, tasks, suspensions,
            new(client, suspensions, audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new([], config, TimeProvider.System, NullLogger<ApprovalService>.Instance), audit, folders,
            new(client, folders, TimeProvider.System, NullLogger<PlanProjectBackup>.Instance));
        try
        {
            JsonElement steps = JsonSerializer.SerializeToElement(new object[]
            {
                new { op = "page.create", args = new { pages = new[] { new { name = page, page_type = "Circuit", description = "P20 created" } }, language = "en_US" } },
                new { op = "page.update", args = new { pages = new[] { new { name = new { from_step = 1, page }, description = "P20 updated" } }, language = "en_US" } },
            });
            CallToolResult compiled = await WorkflowTools.PlanAsync(client, packs, store, trust, tasks, server, project, steps,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(compiled.IsError == true, string.Join("\n", compiled.Content.OfType<TextContentBlock>().Select(c => c.Text)));
            string id = compiled.StructuredContent!.Value.GetProperty("planId").GetString()!;
            using PlanPreparation prepared = await orchestrator.PrepareAsync(server, id, false, null, TestContext.Current.CancellationToken);
            Assert.Equal(PlanStatus.Running, prepared.Record.State.Status);
            Assert.Equal(PolicyVerdict.Allow, prepared.Record.State.Policy);
            PlanRecord completed = await orchestrator.ExecuteAsync(server, prepared, null, TestContext.Current.CancellationToken);
            PlanResult result = completed.State.Result!;
            Assert.Equal(conflict ? PlanExecution.PartiallyCommitted : PlanExecution.Committed, result.Execution);
            Assert.Equal(ExecutionState.Committed, result.Steps[0].Execution);
            Assert.Equal(conflict ? ExecutionState.NotStarted : ExecutionState.Committed, result.Steps[1].Execution);
            Assert.Equal(conflict ? 1 : 2, client.StepWrites);
            if (conflict)
            {
                Assert.Equal(ErrorReason.PlanStale, result.Error!.Reason);
                Assert.Contains("external_between_steps", result.Diff!.Value.GetRawText(), StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("passed", result.Verification);
            }
            PlanBackup initial = Assert.Single(result.Backups);
            Assert.True(initial.Usable);
            Assert.True(File.Exists(initial.Path));
            LiveSnapshots.Snapshot copy = await LiveSnapshots.TakeAsync(bridge, new() { Project = initial.Path, OpenReadOnly = true });
            Assert.Equal(before.Pages.Count, copy.Pages.Count);
            Assert.Equal(before.Pages.Select(p => (p.Name, p.Hash)).OrderBy(p => p.Name, StringComparer.Ordinal),
                copy.Pages.Select(p => (p.Name, p.Hash)).OrderBy(p => p.Name, StringComparer.Ordinal));
            Assert.DoesNotContain(copy.Pages, p => p.Name == page);
            TestContext.Current.TestOutputHelper!.WriteLine($"{result.Execution}; verification={result.Verification}; retained backup: {initial.Path}");
        }
        finally
        {
            LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, new() { Project = project });
            if (after.Pages.Any(p => p.Name == page))
            {
                var args = new PageDeleteArgs { Project = project, Pages = [page] };
                args.Fingerprint = (await bridge.InvokeAsync<PageDeleteArgs, WritePlan>(OperationNames.PageDeletePlan, args, TestContext.Current.CancellationToken)).Fingerprint;
                args.Backup = new() { Path = folders.Create(project), MaxSeconds = 60 };
                await bridge.InvokeWriteAsync<PageDeleteArgs, JsonElement>(OperationNames.PageDeleteExecute, args, Guid.NewGuid().ToString("N"),
                    TimeSpan.FromSeconds(120), null, TestContext.Current.CancellationToken);
            }
        }
    }

    private static async Task UpdateAsync(BridgeClient bridge, string project, string page, string text)
    {
        var args = new PageUpdateArgs { Project = project, Language = "en_US", Pages = [new() { Name = page, Description = text }] };
        args.Fingerprint = (await bridge.InvokeAsync<PageUpdateArgs, WritePlan>(OperationNames.PageUpdatePlan, args, TestContext.Current.CancellationToken)).Fingerprint;
        await bridge.InvokeWriteAsync<PageUpdateArgs, JsonElement>(OperationNames.PageUpdateExecute, args, Guid.NewGuid().ToString("N"),
            TimeSpan.FromSeconds(60), null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Test-only intervention between the steps: on the first status check after step 1's after-content read,
    /// so the change is external to both writes and step 2's guard must refuse it.
    /// </summary>
    private sealed class InterveningBridge(IBridgeClient inner, Func<Task>? intervene) : IBridgeClient
    {
        private bool _intervened;
        private bool _afterRead;

        public int StepWrites { get; private set; }
        public Task<BridgeStatus> GetStatusAsync(bool refresh, CancellationToken cancellationToken) => inner.GetStatusAsync(refresh, cancellationToken);

        public async Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            // Version 2 after-evidence also checks status while reading names, so wait for the content read first.
            if (StepWrites == 1 && _afterRead && intervene is not null && !_intervened)
            {
                _intervened = true;
                await intervene();
            }
            return await inner.GetStatusAsync(cancellationToken);
        }

        public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => inner.SelectAsync(processId, cancellationToken);
        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken) =>
            Observe(args, inner.InvokeAsync<TArgs, TData>(operation, args, cancellationToken));

        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) =>
            Observe(args, inner.InvokeAsync<TArgs, TData>(operation, args, timeout, progress, cancellationToken));

        private async Task<TData> Observe<TArgs, TData>(TArgs args, Task<TData> call)
        {
            TData result = await call;
            if (StepWrites == 1 && args is ProjectSnapshotArgs)
            {
                _afterRead = true;
            }
            return result;
        }
        public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => inner.FetchOutcomeAsync(processId, requestId, cancellationToken);
        public async Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
        {
            WriteResult<TData> result = await inner.InvokeWriteAsync<TArgs, TData>(operation, args, requestId, limit, progress, cancellationToken);
            if (operation == OperationNames.PlanStepExecute)
            {
                StepWrites++;
            }
            return result;
        }
    }
}
