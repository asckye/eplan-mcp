using System.Collections.Immutable;
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

/// <summary>LP-6 dry runs only: neither fixture is changed, backed up, opened or closed by these tests.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlanLocalApplyChecksTests(LiveEplan eplan)
{
    [Fact]
    public Task LP6_Sample_Version2LocalCompilationAndApplyDryRun_ReadOnlyWithTimings() => RunAsync(eplan.ProjectPath, false);

    [Fact]
    public async Task LP6_3000PageFixture_Version2LocalCompilationAndApplyDryRun_ReadOnlyWithTimings()
    {
        string? fixture = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_FIXTURE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(fixture), "Set EPLANMCP_LARGE_FIXTURE to an already open scratch 3,000-page fixture copy.");
        Assert.True(Path.IsPathFullyQualified(fixture));
        Assert.True(ScratchPaths.IsInside(fixture, [LiveEplan.ScratchRoot()]));
        // Accept the path with or without .elk; the design files are named after the extensionless project path.
        await RunAsync(fixture.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? fixture[..^4] : fixture, true);
    }

    private async Task RunAsync(string project, bool large)
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        ProjectListData open = await bridge.InvokeAsync<object, ProjectListData>(OperationNames.ProjectList, new(), Token);
        Assert.Contains(open.Projects, p => PlanSnapshotHashes.ProjectKey(p.Path) == PlanSnapshotHashes.ProjectKey(project));
        PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(project, Token);
        if (large)
        {
            Assert.Equal(3000, names.Names.Count(n => n.Kind == "page"));
        }
        PlanFunction[] device = await DeviceAsync(bridge, project, names);
        string[] pages = [.. device.Select(f => f.Page!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(project);
        var recorded = new ReadOnlyBridge(bridge);
        var input = new PlanArgumentStep(1, "device.update", JsonSerializer.SerializeToElement(new
        {
            tag = device[0].Name,
            new_tag = "-LP6_" + Guid.NewGuid().ToString("N"),
        }), ToolTier.Write, []);
        PlanStep? step = null;
        var compileTime = Stopwatch.StartNew();
        PlanBaselineRead read = await new PlanLocalBaselineReader(recorded).ReadAsync(project,
            [new("device.update", PlanScopeRules.Pages([]), device[0].Name)], async (baseline, impacts) =>
            {
                WritePlan plan = await new PlanStepCompiler(recorded).PlanAsync(baseline.Snapshot.Project.Path, input, Token);
                PlanImpact impact = PlanLocalScopeRules.Build(baseline, Assert.Single(impacts), plan);
                step = new(input.Index, input.Op, input.Args, input.Tier, impact.ReadScope, impact.ModifyScope, impact.Preconditions,
                    [], [.. plan.Changes.Select(c => new PlanChange(c.Action, c.Kind, c.Name, c.Property, c.OldValue, c.NewValue))], [], null, true)
                {
                    Details = (plan.Details ?? []).ToImmutableDictionary(StringComparer.Ordinal),
                };
            }, Token);
        compileTime.Stop();
        Assert.Null(read.Reason);
        PlanBaseline stable = Assert.IsType<PlanBaseline>(read.Baseline);
        Assert.Equal(pages, stable.Snapshot.Pages.Select(p => p.Name).Order(StringComparer.Ordinal));
        recorded.AssertPages(pages);
        recorded.Print("compile", compileTime.Elapsed);
        recorded.Clear();

        var locations = new HostDataLocations(Directory.CreateTempSubdirectory("eplanmcp-lp6-readonly-").FullName);
        try
        {
            var audit = new AuditLog(locations.Audit, TimeProvider.System);
            var config = HostConfig.Defaults(locations);
            var packs = new PackLibrary(locations, config, audit, TimeProvider.System);
            var store = new PlanStore(locations, audit);
            var trust = new PlanTrust();
            using var tasks = new TaskRegistry(TimeProvider.System);
#pragma warning disable CA2000 // McpServer owns the transport and closes it with this test session.
            await using McpServer server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
            {
                ServerInfo = new() { Name = "LP6 read-only Large", Version = "1" },
            });
#pragma warning restore CA2000
            BridgeStatus status = await recorded.GetStatusAsync(Token);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(stable, PlanJson.Options);
            string hash = PlanStore.Hash(bytes);
            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            string changes = PlanDiffPreview.For([step!]);
            var definition = new PlanDefinition(Guid.NewGuid().ToString("N"), stable.Snapshot.Project, "default", tasks.OwnerFor(server),
                now, now.AddHours(24), 1, "2", 1, status.Handshake!.Eplan.Version, status.Handshake.Eplan.Variant!,
                [OperationNames.ProjectList, OperationNames.PlanNames, OperationNames.PlanFunctions, OperationNames.ProjectSnapshot,
                    OperationNames.DeviceUpdatePlan, OperationNames.DeviceUpdateExecute, OperationNames.ProjectBackup],
                new([new(step!.Op, step.Args)]), null, null, "sha256:" + hash, [], [],
                new(stable.Snapshot.ComposedHash, stable.Snapshot.MembersHash, stable.NamesHash, PlanStore.Hash(Encoding.UTF8.GetBytes(changes))), [step]);
            await store.PublishAsync(definition, changes, new Dictionary<string, byte[]> { [hash] = bytes }, Token);
            trust.Register(definition);
            var suspensions = SuspensionStore.For(locations);
            var folders = new BackupFolders(locations, TimeProvider.System);
            var orchestrator = new PlanOrchestrator(recorded, packs, store, trust, tasks, suspensions,
                new(recorded, suspensions, audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
                new([], config, TimeProvider.System, NullLogger<ApprovalService>.Instance), audit, folders,
                new(recorded, folders, TimeProvider.System, NullLogger<PlanProjectBackup>.Instance));
            string before = JsonSerializer.Serialize(await store.ReadAsync(definition.PlanId, Token), PlanJson.Options);
            var dryTime = Stopwatch.StartNew();
            using PlanPreparation dry = await orchestrator.PrepareAsync(server, definition.PlanId, true, null, Token);
            dryTime.Stop();

            Assert.Null(dry.Task);
            Assert.Null(dry.Record.State.AttemptId);
            Assert.Equal(0, dry.Record.State.Revision);
            Assert.Equal(before, JsonSerializer.Serialize(await store.ReadAsync(definition.PlanId, Token), PlanJson.Options));
            Assert.False(Directory.Exists(locations.Backups));
            recorded.AssertPages(pages);
            Assert.Equal(1, recorded.Count(OperationNames.PlanNames));
            Assert.Equal(1, recorded.Count(OperationNames.PlanFunctions));
            Assert.Equal(1, recorded.Count(OperationNames.DeviceUpdatePlan));
            recorded.Print("apply dry-run", dryTime.Elapsed);
            Assert.Equal(files, LiveSnapshots.DesignFiles(project));
            TestContext.Current.TestOutputHelper?.WriteLine($"LP-6 fixture pages={names.Names.Count(n => n.Kind == "page")}, names={names.Names.Count}, observed pages={pages.Length}, closure={device.Length}");
        }
        finally
        {
            Directory.Delete(locations.Root, recursive: true);
        }
    }

    private static async Task<PlanFunction[]> DeviceAsync(BridgeClient bridge, string project, PlanNamesData names)
    {
        foreach (string[] batch in names.Names.Where(n => n.Kind == "function").Select(n => n.Name)
            .OrderBy(n => n != "=F+A2-KF1").ThenBy(n => n, StringComparer.Ordinal).Chunk(200))
        {
            PlanFunctionsData data = await bridge.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
                new() { Project = project, Names = [.. batch] }, Token);
            Assert.True(data.Complete);
            var device = data.Functions.GroupBy(f => f.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Any(f => f.IsMain)
                && g.Count() <= 200 && g.All(f => !string.IsNullOrEmpty(f.Page))
                && g.Select(f => f.Page).Distinct(StringComparer.Ordinal).Count() is >= 1 and <= 50);
            if (device is not null)
            {
                return [.. device];
            }
        }
        Assert.Fail("The complete fixture inventory must contain a placed device for the LP-6 dry run.");
        return [];
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class ReadOnlyBridge(IBridgeClient inner) : IBridgeClient
    {
        private readonly List<(string Operation, double Milliseconds)> _calls = [];
        private readonly List<ProjectSnapshotArgs> _snapshots = [];

        public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => inner.GetStatusAsync(cancellationToken);

        public async Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken)
        {
            Assert.Contains(operation, new[] { OperationNames.ProjectList, OperationNames.PlanNames, OperationNames.PlanFunctions,
                OperationNames.ProjectSnapshot, OperationNames.DeviceUpdatePlan });
            if (operation == OperationNames.ProjectSnapshot)
            {
                ProjectSnapshotArgs snapshot = Assert.IsType<ProjectSnapshotArgs>(args);
                Assert.NotNull(snapshot.Pages);
                Assert.NotEmpty(snapshot.Pages);
                _snapshots.Add(snapshot);
            }
            var elapsed = Stopwatch.StartNew();
            TData data = await inner.InvokeAsync<TArgs, TData>(operation, args, cancellationToken);
            _calls.Add((operation, elapsed.Elapsed.TotalMilliseconds));
            return data;
        }

        public int Count(string operation) => _calls.Count(c => c.Operation == operation);

        public void AssertPages(string[] pages)
        {
            Assert.NotEmpty(_snapshots);
            Assert.All(_snapshots, s => Assert.Equal(pages, s.Pages));
        }

        public void Clear()
        {
            _calls.Clear();
            _snapshots.Clear();
        }

        public void Print(string phase, TimeSpan elapsed)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"LP-6 {phase}: total={elapsed.TotalMilliseconds:F1} ms"));
            foreach (var group in _calls.GroupBy(c => c.Operation, StringComparer.Ordinal))
            {
                TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{group.Key}: calls={group.Count()}, total={group.Sum(c => c.Milliseconds):F1} ms"));
            }
        }

        public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
