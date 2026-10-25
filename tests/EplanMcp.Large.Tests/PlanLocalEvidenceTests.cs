using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace EplanMcp.Large.Tests;

/// <summary>LP-7 direct orchestration; compiler version 2 is test-shaped until LP-9 enables compilation.</summary>
[Trait("Size", "Large")]
public sealed partial class PlanLocalEvidenceTests(LiveEplan eplan)
{
    [Fact]
    public async Task LP7_LargeWrite_TwoStepPlan_ReadsOnlyE_PrintsTimings()
    {
        Assert.SkipWhen(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EPLANMCP_LARGE_FIXTURE")),
            "Run the LargeWrite test without EPLANMCP_LARGE_FIXTURE.");
        string project = await eplan.WriteProjectAsync();
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(project));
        Assert.True(ScratchPaths.IsInside(project, [WriteHost.ScratchFolder]));
        await RunAsync(project, null);
    }

    [Fact]
    public async Task LP7_ThreeThousandPages_TwoStepPlan_ReadsOnlyE_PrintsTimings()
    {
        string? fixture = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_FIXTURE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(fixture), "Set EPLANMCP_LARGE_FIXTURE to the only open 3,000-page scratch copy.");
        Assert.True(Path.IsPathFullyQualified(fixture));
        Assert.True(ScratchPaths.IsInside(fixture, [LiveEplan.ScratchRoot()]));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        ProjectListData projects = await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), Token);
        OpenProject only = Assert.Single(projects.Projects);
        Assert.Equal(PlanSnapshotHashes.ProjectKey(fixture), PlanSnapshotHashes.ProjectKey(only.Path));
        await RunAsync(only.Path, 3000);
    }

    private async Task RunAsync(string project, int? expectedPages, string? boundary = null)
    {
        if (boundary is not null)
        {
            Assert.Equal(WriteHost.WritableName, Path.GetFileName(project));
            Assert.True(ScratchPaths.IsInside(project, [WriteHost.ScratchFolder]));
        }
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PlanNamesData inventory = await new PlanNamesReader(bridge).ReadAsync(project, Token);
        int pageCount = inventory.Names.Count(n => n.Kind == "page");
        if (expectedPages is { } count)
        {
            Assert.Equal(count, pageCount);
        }
        int next = inventory.Names.Where(n => n.Kind == "page" && n.Name.StartsWith(DestructiveHost.Prefix, StringComparison.Ordinal))
            .Select(n => DestructiveHost.Number(n.Name)).DefaultIfEmpty().Max() + 100;
        string page = DestructiveHost.Prefix + next.ToString(CultureInfo.InvariantCulture);
        string far = DestructiveHost.Prefix + (next + 1).ToString(CultureInfo.InvariantCulture);
        var client = new RecordingBridge(bridge, page);
        var locations = new HostDataLocations(Directory.CreateTempSubdirectory("eplanmcp-lp7-large-").FullName);
        HostConfig config = HostConfig.Defaults(locations) with { ScratchRoots = [LiveEplan.ScratchRoot()] };
        var audit = new AuditLog(locations.Audit, TimeProvider.System);
        var packs = new PackLibrary(locations, config, audit, TimeProvider.System);
        var store = new PlanStore(locations, audit);
        var trust = new PlanTrust();
        using var tasks = new TaskRegistry(TimeProvider.System);
#pragma warning disable CA2000 // McpServer owns the transport for the test session.
        await using McpServer server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        {
            ServerInfo = new() { Name = "LP7 Large", Version = "1" },
        });
#pragma warning restore CA2000
        var suspensions = SuspensionStore.For(locations);
        var folders = new BackupFolders(locations, TimeProvider.System);
        var orchestrator = new PlanOrchestrator(client, packs, store, trust, tasks, suspensions,
            new(client, suspensions, audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new([], config, TimeProvider.System, NullLogger<ApprovalService>.Instance), audit, folders,
            new(client, folders, TimeProvider.System, NullLogger<PlanProjectBackup>.Instance));
        try
        {
            if (boundary == "description")
            {
                await CreateBoundaryPageAsync(bridge, project, far);
                pageCount++;
            }
            if (boundary is not null)
            {
                client.BetweenSteps = () => boundary == "names"
                    ? CreateBoundaryPageAsync(bridge, project, far) : UpdateBoundaryPageAsync(bridge, project, far);
            }
            var clock = Stopwatch.StartNew();
            JsonElement createArgs = JsonSerializer.SerializeToElement(new
            {
                pages = new[] { new { name = page, page_type = "Circuit", description = "LP7 created" } },
                language = "en_US",
            });
            JsonElement updateArgs = JsonSerializer.SerializeToElement(new
            {
                pages = new[] { new { name = page, description = "LP7 updated" } },
                language = "en_US",
            });
            PlanStep? create = null;
            PlanStep? update = null;
            PlanBaselineRead read = await new PlanLocalBaselineReader(client).ReadAsync(project,
                [new("page.create", PlanScopeRules.Pages([page]))], async (baseline, impacts) =>
                {
                    var input = new PlanArgumentStep(1, "page.create", createArgs, ToolTier.Write, []);
                    WritePlan fresh = await new PlanStepCompiler(client).PlanAsync(project, input, Token);
                    create = PlanStepSemantics.Build(baseline, input, PlanLocalScopeRules.Build(baseline, Assert.Single(impacts), fresh), fresh);
                    update = await new PlanDependencyPredictor(client).PredictAsync(baseline,
                        new(2, "page.update", updateArgs, ToolTier.Write, [1]), create, Token);
                }, Token);
            Assert.Empty(read.Conflicts);
            PlanBaseline baseline = Assert.IsType<PlanBaseline>(read.Baseline);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(baseline, PlanJson.Options);
            string hash = PlanStore.Hash(bytes);
            HandshakeResult handshake = (await client.GetStatusAsync(Token)).Handshake!;
            Assert.NotNull(handshake.Eplan.Variant);
            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            var definition = new PlanDefinition(Guid.NewGuid().ToString("N"), baseline.Snapshot.Project, "default", tasks.OwnerFor(server),
                now, now.AddHours(24), 1, "2", 1, handshake.Eplan.Version, handshake.Eplan.Variant,
                [OperationNames.PlanNames, OperationNames.ProjectSnapshot, OperationNames.PlanStepExecute, OperationNames.OutcomeGet,
                    OperationNames.ProjectBackup, OperationNames.PageCreatePlan, OperationNames.PageUpdatePlan],
                new([new("page.create", createArgs), new("page.update", updateArgs)]), null, null, "sha256:" + hash, [], [],
                new(baseline.Snapshot.ComposedHash, baseline.Snapshot.MembersHash, baseline.NamesHash, null), [create!, update!]);
            string changes = PlanDiffPreview.For(definition.Steps);
            definition = definition with { Digests = definition.Digests with { ChangesDigest = PlanStore.Hash(Encoding.UTF8.GetBytes(changes)) } };
            await store.PublishAsync(definition, changes, new Dictionary<string, byte[]> { [hash] = bytes }, Token);
            trust.Register(definition);
            Print($"pages={pageCount}; compile={clock.Elapsed.TotalSeconds:F3}s");
            Assert.Empty(client.Snapshots);
            clock.Restart();
            using PlanPreparation prepared = await orchestrator.PrepareAsync(server, definition.PlanId, false, null, Token);
            Print($"prepare/backup={clock.Elapsed.TotalSeconds:F3}s");
            Assert.Equal(PlanStatus.Running, prepared.Record.State.Status);
            Assert.Empty(client.Snapshots);
            clock.Restart();
            PlanResult result = (await orchestrator.ExecuteAsync(server, prepared, null, Token)).State.Result!;
            Print($"execute={clock.Elapsed.TotalSeconds:F3}s; evidence={Directory.EnumerateFiles(Path.Combine(locations.State, "plans", definition.PlanId, "evidence")).Sum(f => new FileInfo(f).Length)} bytes");
            Assert.Equal(boundary == "names" ? PlanExecution.PartiallyCommitted : PlanExecution.Committed, result.Execution);
            if (boundary == "names")
            {
                Assert.Equal(ErrorReason.PlanStale, result.Error!.Reason);
                Assert.Equal(ExecutionState.Committed, result.Steps[0].Execution);
                Assert.Equal(ExecutionState.NotStarted, result.Steps[1].Execution);
                Assert.Contains(result.Diff!.Value.EnumerateArray(), d => d.GetProperty("class").GetString() == "external_between_steps"
                    && d.GetProperty("locator").GetProperty("name").GetString() == far);
            }
            else
            {
                Assert.Equal("passed", result.Verification);
                Assert.All(result.Steps, s => Assert.Equal(ExecutionState.Committed, s.Execution));
            }
            Assert.Equal(boundary == "names" ? 1 : 2, client.StepWrites);
            Assert.Equal(boundary == "names" ? 2 : 3, client.Snapshots.Count);
            Assert.All(client.Snapshots, requested => Assert.Equal([page], requested));
            PlanBackup backup = Assert.Single(result.Backups);
            Assert.True(backup.Usable);
            Assert.Equal(pageCount, backup.Pages);
            Assert.True(File.Exists(backup.Path));
            JsonElement refs = result.Coverage!.Value.GetProperty("evidence");
            Assert.Equal(refs.GetProperty("1:after").GetString(), refs.GetProperty("2:before").GetString());
            byte[] observed = await store.ReadEvidenceAsync(definition.PlanId, refs.GetProperty("1:after").GetString()!, Token);
            using JsonDocument parsed = JsonDocument.Parse(observed);
            Assert.False(parsed.RootElement.GetProperty("wholeProject").GetBoolean());
            if (boundary is not null)
            {
                PlanRecord completed = (await store.ReadAsync(definition.PlanId, Token))!;
                var coverage = await PlanCoverage.ReadAsync(store, completed, Token);
                Assert.Contains(coverage, c => c.UnobservedPages.Contains(far, StringComparer.Ordinal));
                Assert.True(JsonSerializer.SerializeToUtf8Bytes(completed.State, PlanJson.Options).Length < PlanCapacity.MiB);
            }
            Print($"{result.Execution}; verification={result.Verification}; retained backup={backup.Path}");
        }
        finally
        {
            PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(project, Token);
            string[] cleanup = [.. new[] { page, far }.Where(candidate => names.Names.Any(n => n.Kind == "page" && n.Name == candidate))];
            if (cleanup.Length > 0)
            {
                var args = new PageDeleteArgs { Project = project, Pages = [.. cleanup] };
                args.Fingerprint = (await bridge.InvokeAsync<PageDeleteArgs, WritePlan>(OperationNames.PageDeletePlan, args, Token)).Fingerprint;
                args.Backup = new() { Path = folders.Create(project), MaxSeconds = 60 };
                await bridge.InvokeWriteAsync<PageDeleteArgs, JsonElement>(OperationNames.PageDeleteExecute, args, Guid.NewGuid().ToString("N"),
                    TimeSpan.FromSeconds(120), null, Token);
                Assert.DoesNotContain((await new PlanNamesReader(bridge).ReadAsync(project, Token)).Names,
                    n => n.Kind == "page" && cleanup.Contains(n.Name, StringComparer.Ordinal));
            }
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static void Print(string message) => TestContext.Current.TestOutputHelper!.WriteLine(message);

    private sealed class RecordingBridge(IBridgeClient inner, string page) : IBridgeClient
    {
        private bool _intervened;
        private bool _firstAfterRead;
        public List<string[]> Snapshots { get; } = [];
        public int StepWrites { get; private set; }
        public Func<Task>? BetweenSteps { get; set; }

        public async Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            if (StepWrites == 1 && _firstAfterRead && !_intervened && BetweenSteps is not null)
            {
                _intervened = true;
                await BetweenSteps();
            }
            return await inner.GetStatusAsync(cancellationToken);
        }
        public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => inner.SelectAsync(processId, cancellationToken);
        public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => inner.FetchOutcomeAsync(processId, requestId, cancellationToken);

        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken) =>
            InvokeAsync<TArgs, TData>(operation, args, TimeSpan.FromSeconds(30), null, cancellationToken);

        public async Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
        {
            if (args is ProjectSnapshotArgs snapshot)
            {
                Assert.NotNull(snapshot.Pages);
                Assert.Equal(SnapshotProfiles.StandardName, snapshot.Profile);
                Assert.Equal([page], snapshot.Pages);
                Snapshots.Add([.. snapshot.Pages]);
            }
            var clock = Stopwatch.StartNew();
            TData result = await inner.InvokeAsync<TArgs, TData>(operation, args, timeout, progress, cancellationToken);
            if (StepWrites == 1 && args is ProjectSnapshotArgs)
            {
                _firstAfterRead = true;
            }
            Print($"{operation}={clock.Elapsed.TotalSeconds:F3}s");
            return result;
        }

        public async Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId,
            TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            WriteResult<TData> result = await inner.InvokeWriteAsync<TArgs, TData>(operation, args, requestId, limit, progress, cancellationToken);
            if (operation == OperationNames.PlanStepExecute)
            {
                StepWrites++;
            }
            Print($"{operation}={clock.Elapsed.TotalSeconds:F3}s");
            return result;
        }
    }
}
