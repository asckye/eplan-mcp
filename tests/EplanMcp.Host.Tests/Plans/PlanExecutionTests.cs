using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Bridge;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using TaskStatus = EplanMcp.Host.Plans.TaskStatus;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed partial class PlanExecutionTests : IAsyncDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-p20-").FullName);
    private readonly ManualTime _clock = TaskTestData.Clock();
    private readonly McpServer _server = TaskTestData.Server();
    private readonly FakeBridgeClient _bridge = new();
    private readonly PlanTrust _trust = new();
    private readonly TaskRegistry _tasks;
    private readonly PlanStore _store;
    private readonly AuditLog _audit;
    private readonly SuspensionStore _suspensions;
    private readonly PlanBaseline _baseline;
    private readonly PlanOrchestrator _service;
    private PlanSnapshot _current;
    private PlanPreparation? _prepared;
    private Action<int>? _after;
    private int _writes;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public PlanExecutionTests()
    {
        _tasks = new(_clock);
        _audit = new(_locations.Audit, _clock) { LockWait = TimeSpan.Zero };
        _store = new(_locations, _audit) { Clock = _clock, Checkpoint = point => _coverageFault?.Invoke(point) };
        _suspensions = SuspensionStore.For(_locations);
        _baseline = PlanScopeTestData.Baseline([], ["+A1/1"], Path.Combine(_locations.DefaultScratch, "Line1"));
        _current = PlanVerificationTestData.Copy(_baseline.Snapshot, _ => { });
        _baseline = _baseline with { Snapshot = _current };
        _bridge.Status = new([new("fake-p20", Environment.ProcessId)], new()
        {
            ProtocolVersion = BridgeProtocol.CurrentVersion,
            Eplan = new() { ProcessId = Environment.ProcessId, Version = "2026", Variant = "Pro Panel" },
            Operations = [OperationNames.PlanStepExecute, OperationNames.OutcomeGet, OperationNames.ProjectSnapshot,
                OperationNames.DeviceList, OperationNames.ProjectBackup, OperationNames.PageCreatePlan, OperationNames.PageUpdatePlan],
        }, null);
        _bridge.Handlers[OperationNames.ProjectSnapshot] = _ => new ProjectSnapshotData
        {
            Project = _current.Project.Name,
            ProjectPath = _current.Project.Path,
            ProjectGuid = _current.Project.Guid,
            Profile = _current.Profile,
            Format = _current.Format,
            Pages = [.. _current.Pages],
            PageCount = _current.Pages.Length,
            SnapshotHash = _current.ComposedHash,
            Complete = true,
        };
        _bridge.Handlers[OperationNames.DeviceList] = _ => new ListData<DeviceListItem> { Project = "Line1" };
        _bridge.Handlers[OperationNames.PageCreatePlan] = _ => Fresh("page.create", "+A1/3", "Power");
        _bridge.Handlers[OperationNames.PageUpdatePlan] = args =>
        {
            PageUpdateArgs update = Assert.IsType<PageUpdateArgs>(args);
            return Fresh("page.update", update.Pages[0].Name, update.Pages[0].Description);
        };
        _bridge.Handlers[OperationNames.ProjectBackup] = args =>
        {
            string path = Assert.IsType<ProjectBackupArgs>(args).Target!.Path;
            File.WriteAllText(path, "initial backup");
            _clock.Advance(TimeSpan.FromSeconds(1));
            return new ProjectBackupData { Path = path, Pages = _current.Pages.Length, Seconds = 0.1m };
        };
        _bridge.Handlers[OperationNames.PlanStepExecute] = args =>
        {
            var step = Assert.IsType<PlanStepExecuteArgs>(args);
            Assert.Single(Records(), r => Kind(r) == "execution_started" && r["step"]!.GetValue<int>() == step.Index);
            Assert.Throws<IOException>(() => new FileStream(Path.Combine(new PackLibrary(_locations, HostConfig.Defaults(_locations), _audit, _clock)
                .Workspaces.FolderOf(_current.Project.Path), ".binding.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            _writes++;
            _current = step.PageCreate is not null ? PlanVerificationTestData.After("page.create", _current)
                : PlanVerificationTestData.Copy(_current, pages => PlanVerificationTestData.Description(
                    pages.Single(p => p.Name == step.PageUpdate!.Pages[0].Name).Objects[0], step.PageUpdate!.Pages[0].Description));
            _after?.Invoke(_writes);
            return JsonSerializer.SerializeToElement(new { });
        };
        _service = ServiceFor(_bridge);
    }

    private PlanOrchestrator ServiceFor(IBridgeClient client)
    {
        HostConfig config = HostConfig.Defaults(_locations);
        var folders = new BackupFolders(_locations, _clock);
        return new(client, new PackLibrary(_locations, config, _audit, _clock), _store, _trust, _tasks, _suspensions,
            new(client, _suspensions, _audit, _clock, NullLogger<DanglingWrites>.Instance),
            new([new FakeApprovalChannel("fake", ApprovalDecision.Granted)], config, _clock, NullLogger<ApprovalService>.Instance),
            _audit, folders, new(client, folders, _clock, NullLogger<PlanProjectBackup>.Instance));
    }

    [Fact]
    public async Task AllCommitted_VerificationAndAuditArePersisted_LockAndTaskFinish()
    {
        PlanRecord result = await Run(Token);

        Assert.Equal(PlanExecution.Committed, result.State.Result!.Execution);
        Assert.Equal("passed", result.State.Result.Verification);
        Assert.All(result.State.Result.Steps, s => Assert.Equal(ExecutionState.Committed, s.Execution));
        Assert.Equal(TaskStatus.Succeeded, Task().Status);
        Assert.Equal(3, _bridge.Writes.Count);
        Assert.Equal(3, _bridge.Writes.Select(w => w.RequestId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, Records().Count(r => Kind(r) == "execution_result"));
        Assert.Single(Records(), r => Kind(r) == "plan_result");
        Assert.Equal(result.State.Revision, (await _store.ReadAsync(result.Definition.PlanId, Token))!.State.Revision);
        Assert.True(_prepared!.ProjectLock!.SafeFileHandle.IsClosed);
        PlanStepExecuteArgs dependent = Assert.IsType<PlanStepExecuteArgs>(_bridge.Calls.Where(c => c.Operation == OperationNames.PlanStepExecute).ElementAt(1).Args);
        Assert.Contains(dependent.Guard.Preconditions, c => c.Tag == "pageHash" && c.Locator!.Name == "+A1/3");
        Assert.Null(dependent.PageUpdate!.Fingerprint);
        Assert.NotEmpty(result.State.Result.Coverage!.Value.GetProperty("evidence").EnumerateObject());
    }

    [Fact]
    public async Task IneffectiveStep_KeepsNotStartedWithoutPreventingSuccessfulVerification()
    {
        await Prepare(noOp: true);
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal("passed", result.Verification);
        Assert.Equal(ExecutionState.NotStarted, result.Steps[2].Execution);
        Assert.False(result.Steps[2].Effective);
        Assert.Equal(2, _bridge.Writes.Count);
        Assert.Equal(TaskStatus.Succeeded, Task().Status);
    }

    [Fact]
    public async Task StepTwoFails_KeepsCommitAndLeavesLaterStepsNotStarted()
    {
        _after = _ => _bridge.WriteFailure = new() { Reason = ErrorReason.LockFailed, Message = "locked", Execution = ExecutionState.RolledBack };
        PlanResult result = (await Run(Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal([ExecutionState.Committed, ExecutionState.RolledBack, ExecutionState.NotStarted], result.Steps.Select(s => s.Execution));
        Assert.Equal(2, _bridge.Writes.Count);
        Assert.False(result.Retryable);
        Assert.StartsWith("AI: plan ", result.Steps[0].UndoDescription, StringComparison.Ordinal);
        Assert.Null(result.Steps[1].UndoDescription);
    }

    [Fact]
    public async Task CancellationBetweenSteps_PreservesCommitAndCompletesCancelledTask()
    {
        using var cancel = new CancellationTokenSource();
        _after = _ => cancel.Cancel();
        PlanResult result = (await Run(cancel.Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal(ErrorReason.Cancelled, result.Error!.Reason);
        Assert.Single(_bridge.Writes);
        Assert.Equal(TaskStatus.Cancelled, Task().Status);
    }

    [Fact]
    public async Task OutsideScopeAlert_StopsImmediately_WithUnknownAttribution()
    {
        _after = _ => _current = PlanVerificationTestData.Copy(_current,
            pages => PlanVerificationTestData.Description(pages[0].Objects[0], "unexpected"));
        PlanResult result = (await Run(Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Single(_bridge.Writes);
        Assert.Contains(result.Diff!.Value.EnumerateArray(), d => d.GetProperty("class").GetString() == "outside_scope"
            && d.GetProperty("attribution").GetString() == "unknown");
    }

    [Fact]
    public async Task AuditFailsAfterCommit_KeepsFactsAndStops_AuditDanglingCanBeSettledLater()
    {
        _after = _ => File.AppendAllText(AuditFile, "damaged\n");
        PlanResult result = (await Run(Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal(ErrorReason.AuditUnavailable, result.Error!.Reason);
        Assert.Equal(ExecutionState.Committed, result.Steps[0].Execution);
        Assert.Single(_bridge.Writes);
        Assert.Single(_audit.Dangling());
    }

    [Fact]
    public async Task ExecutionStartedAuditUnavailable_SendsNoWrite()
    {
        await Prepare();
        using var held = new FileStream(Path.Combine(_locations.Audit, ".lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.NotStarted, result.Execution);
        Assert.Equal(ErrorReason.AuditUnavailable, result.Error!.Reason);
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task EvidenceCapacityAfterCommit_StopsAndKeepsExecution_VerificationSkipped()
    {
        _after = _ =>
        {
            string file = Path.Combine(_locations.State, "plans", _prepared!.Record.Definition.PlanId, "full.bin");
            using var stream = new FileStream(file, FileMode.CreateNew);
            stream.SetLength(PlanCapacity.PerPlan);
        };
        PlanResult result = (await Run(Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal(ErrorReason.ResourceExhausted, result.Error!.Reason);
        Assert.Equal("skipped", result.Verification);
        Assert.Single(_bridge.Writes);
    }

    [Fact]
    public async Task SuspensionBetweenSteps_StopsWithoutAskingAgain()
    {
        _after = _ => _suspensions.Suspend(_current.Project.Path, Environment.ProcessId,
            SuspensionStore.StartOf(Environment.ProcessId)!.Value, "another_write", "other", _clock.GetUtcNow(), SuspensionReason.OutcomeUnknown, []);
        PlanResult result = (await Run(Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal(ErrorReason.WritesSuspended, result.Error!.Reason);
        Assert.Single(_bridge.Writes);
    }

    [Fact]
    public async Task TerminalStateWriteFails_ReturnsInMemoryCommits_AndFinishesTask()
    {
        _after = count =>
        {
            if (count == 3)
            {
                _clock.Advance(TimeSpan.FromSeconds(1));
                string file = Path.Combine(_locations.State, "plans", _prepared!.Record.Definition.PlanId + ".json");
                PlanRecord current = PlanJson.Read<PlanRecord>(File.ReadAllText(file));
                File.WriteAllText(file, JsonSerializer.Serialize(current with { State = current.State with { Revision = current.State.Revision + 1 } }, PlanJson.Options));
            }
        };
        PlanResult result = (await Run(Token)).State.Result!;

        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal(ErrorReason.Internal, result.Error!.Reason);
        Assert.Equal(TaskStatus.Failed, Task().Status);
        Assert.True(_prepared!.ProjectLock!.SafeFileHandle.IsClosed);
    }

    [Fact]
    public async Task RecoveredCommit_StopsAfterThatStep_WithoutInventedData()
    {
        await Prepare();
        _bridge.WriteRecovered = true;
        _bridge.DuringWrite = _ => _current = PlanVerificationTestData.After("page.create", _current);
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.True(result.Steps[0].Recovered);
        Assert.Single(_bridge.Writes);
        Assert.Empty(_suspensions.All());
    }

    [Fact]
    public async Task OutcomeMissing_IsUnknown_SuspendsProjectAndInstance_EvenIfAuditFails()
    {
        await Prepare();
        _bridge.WriteFailure = BridgeErrors.OutcomeUnknown();
        _bridge.WriteFailure.Execution = ExecutionState.Unknown;
        _bridge.DuringWrite = _ => File.AppendAllText(AuditFile, "damaged\n");
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.Unknown, result.Execution);
        Assert.Equal(ErrorReason.OutcomeUnknown, result.Error!.Reason);
        Assert.Equal(2, _suspensions.Blocking(_baseline.Snapshot.Project.Path, Environment.ProcessId, SuspensionStore.StartOf(Environment.ProcessId)!.Value).Count);
        Assert.Equal(TaskStatus.Unknown, Task().Status);
        Assert.Single(_bridge.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteTimeout_RealClientFetchesSameRequestOutcome_NeverResends(bool committed)
    {
        await Prepare();
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(new("fake-p20", Environment.ProcessId));
        string? sent = null;
        int executes = 0;
        int queries = 0;
        var connector = new FakeConnector(endpoint => new FakeChannel(endpoint, async (_, envelope, token) =>
        {
            string operation = envelope!["operation"]!.GetValue<string>();
            if (operation == OperationNames.OutcomeGet)
            {
                queries++;
                Assert.Equal(sent, envelope["args"]!["requestId"]!.GetValue<string>());
                return new JsonObject
                {
                    ["data"] = JsonSerializer.SerializeToNode(new OutcomeGetData
                    {
                        Operation = OperationNames.PlanStepExecute,
                        Status = committed ? OutcomeStatus.Committed : OutcomeStatus.Expired,
                    }, HostJson.Options)
                };
            }
            Assert.Equal(OperationNames.PlanStepExecute, operation);
            executes++;
            sent = envelope["correlationId"]!.GetValue<string>();
            if (committed)
            {
                _current = PlanVerificationTestData.After("page.create", _current);
            }
            await System.Threading.Tasks.Task.Delay(Timeout.Infinite, token);
            return null;
        })
        {
            Handshake = (_, _, _) => System.Threading.Tasks.Task.FromResult(JsonSerializer.SerializeToNode(_bridge.Status.Handshake, HostJson.Options)),
        });
        await using var real = new BridgeClient(discovery, connector, NullLogger<BridgeClient>.Instance,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        PlanResult result = (await ServiceFor(new WriteTransport(_bridge, real)).ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(committed ? PlanExecution.PartiallyCommitted : PlanExecution.Unknown, result.Execution);
        Assert.Equal(committed, result.Steps[0].Recovered);
        Assert.Equal(1, executes);
        Assert.Equal(1, queries);
        Assert.Equal(sent, Assert.Single(Records(), r => Kind(r) == "execution_started")["requestId"]!.GetValue<string>());
        Assert.Equal(committed ? 0 : 2, _suspensions.All().Count);
        Assert.Equal(committed ? 0 : 1, Records().Count(r => Kind(r) == "writes_suspended"));
    }

    [Fact]
    public async Task BudgetSpentDuringPlanning_DoesNotSendStep()
    {
        await Prepare();
        _clock.Advance(TimeSpan.FromMinutes(14));
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.NotStarted, result.Execution);
        Assert.Equal(ErrorReason.Timeout, result.Error!.Reason);
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task FinishedPlan_CannotReplayPreparationOrBePreparedAgain()
    {
        PlanRecord finished = await Run(Token);
        Assert.Equal(ErrorReason.InvalidArgument, (await Assert.ThrowsAsync<BridgeCallException>(() =>
            _service.ExecuteAsync(_server, _prepared!, null, Token))).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, (await Assert.ThrowsAsync<BridgeCallException>(() =>
            _service.PrepareAsync(_server, finished.Definition.PlanId, false, null, Token))).Error.Reason);
        Assert.Equal(3, _bridge.Writes.Count);
    }

    [Fact]
    public async Task DestructiveStep_GetsAdditionalNewBackupTarget()
    {
        await Prepare(destructive: true);
        _bridge.Handlers[OperationNames.PlanStepExecute] = args =>
        {
            var step = Assert.IsType<PlanStepExecuteArgs>(args);
            Assert.NotNull(step.Backup);
            Assert.NotEqual(_prepared!.Backup!.Path, step.Backup.Path);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(step.Backup.Path)!));
            Assert.Equal(31, step.Backup.MaxSeconds);
            File.WriteAllText(step.Backup.Path, "step backup");
            _current = PlanVerificationTestData.Copy(_current, pages => pages.Clear());
            return JsonSerializer.SerializeToElement(new { });
        };
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal(2, result.Backups.Length);
        Assert.All(result.Backups, b => Assert.True(b.Usable));
    }

    private async Task<PlanRecord> Run(CancellationToken cancellationToken)
    {
        await Prepare();
        return await _service.ExecuteAsync(_server, _prepared!, null, cancellationToken);
    }

    private async Task Prepare(bool destructive = false, bool noOp = false, bool local = false, PlanOccupiedName[]? baselineNames = null)
    {
        PlanScope scope = PlanScopeRules.Pages(["+A1/1"]);
        JsonElement createArgs = PlanTestData.Json("""{"pages":[{"name":"+A1/3","page_type":"Circuit","description":"Power"}],"language":"en_US"}""");
        var input = new PlanArgumentStep(1, "page.create", createArgs, ToolTier.Write, []);
        PlanStep first = PlanStepSemantics.Build(_baseline, input, new(scope, PlanScopeRules.Pages(["+A1/3"]),
            PlanPreconditions.Build(_baseline, scope, [new("page", "+A1/3")]), _baseline.NamesHash, []), Fresh("page.create", "+A1/3", "Power"));
        if (local)
        {
            first = first with
            {
                ReadScope = PlanScopeRules.Pages([]),
                Preconditions = PlanPreconditions.Build(_baseline, PlanScopeRules.Pages([]), [new("page", "+A1/3")]),
            };
        }
        JsonElement updateArgs = PlanTestData.Json("""{"pages":[{"name":"+A1/3","description":"New"}],"language":"en_US"}""");
        PlanStep second = await new PlanDependencyPredictor(_bridge).PredictAsync(_baseline, new(2, "page.update", updateArgs, ToolTier.Write, [1]), first, Token);
        JsonElement thirdArgs = PlanTestData.Json("""{"pages":[{"name":"+A1/1","description":"Last"}],"language":"en_US"}""");
        PlanStep third = PlanStepSemantics.Build(_baseline, new(3, "page.update", thirdArgs, ToolTier.Write, []),
            new(scope, scope, PlanPreconditions.Build(_baseline, scope), _baseline.NamesHash, []), Fresh("page.update", "+A1/1", "Last"));
        if (noOp)
        {
            WritePlan empty = Fresh("page.update", "+A1/1", "");
            empty.Changes.Clear();
            var update = _bridge.Handlers[OperationNames.PageUpdatePlan];
            _bridge.Handlers[OperationNames.PageUpdatePlan] = a => Assert.IsType<PageUpdateArgs>(a).Pages[0].Name == "+A1/1" ? empty : update(a);
            thirdArgs = PlanTestData.Json("""{"pages":[{"name":"+A1/1","description":""}],"language":"en_US"}""");
            third = PlanStepSemantics.Build(_baseline, new(3, "page.update", thirdArgs, ToolTier.Write, []),
                new(scope, scope, PlanPreconditions.Build(_baseline, scope), _baseline.NamesHash, []), empty);
        }
        if (destructive)
        {
            JsonElement deleteArgs = PlanTestData.Json("""{"pages":["+A1/1"]}""");
            _bridge.Handlers[OperationNames.PageDeletePlan] = _ => Fresh("page.delete", "+A1/1", "");
            _bridge.Status.Handshake!.Operations!.Add(OperationNames.PageDeletePlan);
            first = PlanStepSemantics.Build(_baseline, new(1, "page.delete", deleteArgs, ToolTier.Destructive, []),
                new(scope, scope, PlanPreconditions.Build(_baseline, scope), _baseline.NamesHash, []), Fresh("page.delete", "+A1/1", ""));
        }
        PlanBaseline storedBaseline = baselineNames is null ? _baseline : _baseline with
        {
            Names = [.. baselineNames],
            NamesHash = PlanSnapshotHashes.Names(_baseline.Snapshot.Project.Path, baselineNames),
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(storedBaseline, PlanJson.Options);
        string hash = PlanStore.Hash(bytes);
        PlanDefinition plan = PlanTestData.Definition() with
        {
            CompilerVersion = local ? "2" : "1",
            Owner = _tasks.OwnerFor(_server),
            Project = _baseline.Snapshot.Project,
            Steps = destructive ? [first] : [first, second, third],
            Resources = [],
            Sources = [],
            RequiredOperations = [.. _bridge.Status.Handshake!.Operations!],
            ContextRef = "sha256:" + hash,
            Digests = new(_baseline.Snapshot.ComposedHash, _baseline.Snapshot.MembersHash, storedBaseline.NamesHash, null),
        };
        string changes = PlanDiffPreview.For(plan.Steps, local);
        plan = plan with { Digests = plan.Digests with { ChangesDigest = PlanStore.Hash(Encoding.UTF8.GetBytes(changes)) } };
        await _store.PublishAsync(plan, changes, new Dictionary<string, byte[]> { [hash] = bytes }, Token);
        _trust.Register(plan);
        _prepared = await _service.PrepareAsync(_server, plan.PlanId, false, null, Token);
        Assert.Equal(PlanStatus.Running, _prepared.Record.State.Status);
    }

    private WritePlan Fresh(string op, string name, string description)
    {
        SnapshotObject? page = _current.Pages.FirstOrDefault(p => p.Name == name)?.Objects[0];
        string? old = page?.Values.SingleOrDefault(v => v.Id == 11011)?.Texts?.SingleOrDefault(t => t.Language == "en_US")?.Text;
        return new()
        {
            Project = "Line1",
            ProjectPath = _current.Project.Path,
            Fingerprint = "temporary",
            Changes = [new() { Action = op == "page.create" ? "create" : op == "page.delete" ? "delete" : "update", Kind = "page", Name = name,
                Property = op == "page.update" ? "description (en_US)" : null, OldValue = old, NewValue = op == "page.update" ? description : null }],
        };
    }

    private TaskRecord Task() => _tasks.Get(_server, _prepared!.Task!.TaskId);
    private string AuditFile => Path.Combine(_locations.Audit, "2026-10-23.jsonl");
    private List<JsonObject> Records() => AuditLog.ReadLines(AuditFile).Select(l => JsonNode.Parse(l)).OfType<JsonObject>().ToList();
    private static string Kind(JsonObject record) => record["kind"]!.GetValue<string>();

    public async ValueTask DisposeAsync()
    {
        _prepared?.Dispose();
        _tasks.Dispose();
        await _server.DisposeAsync();
        Directory.Delete(_locations.Root, recursive: true);
    }

    private sealed class WriteTransport(IBridgeClient reads, BridgeClient writes) : IBridgeClient
    {
        public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => reads.GetStatusAsync(cancellationToken);
        public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => reads.SelectAsync(processId, cancellationToken);
        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken) => reads.InvokeAsync<TArgs, TData>(operation, args, cancellationToken);
        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) => reads.InvokeAsync<TArgs, TData>(operation, args, timeout, progress, cancellationToken);
        public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => writes.FetchOutcomeAsync(processId, requestId, cancellationToken);
        public Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) =>
            writes.InvokeWriteAsync<TArgs, TData>(operation, args, requestId, TimeSpan.FromMilliseconds(30), progress, cancellationToken);
    }
}
