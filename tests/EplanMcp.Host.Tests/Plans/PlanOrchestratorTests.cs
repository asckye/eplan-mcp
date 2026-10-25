using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using TaskStatus = EplanMcp.Host.Plans.TaskStatus;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed partial class PlanOrchestratorTests : IAsyncDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-orchestrator-").FullName);
    private readonly ManualTime _clock = TaskTestData.Clock();
    private readonly McpServer _server = TaskTestData.Server();
    private readonly FakeBridgeClient _bridge = new();
    private readonly PlanTrust _trust = new();
    private readonly TaskRegistry _tasks;
    private readonly PlanStore _store;
    private readonly AuditLog _audit;
    private readonly SuspensionStore _suspensions;
    private readonly HostConfig _config;
    private readonly WritePlan _fresh;
    private readonly PlanDefinition _definition;
    private FakeApprovalChannel _channel = new("fake", ApprovalDecision.Granted);
    private IApprovalChannel? _deferred;
    private string? _backupPath;
    private PlanPreparation? _ready;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public PlanOrchestratorTests()
    {
        _tasks = new(_clock);
        _audit = new(_locations.Audit, _clock);
        _store = new(_locations, _audit) { Clock = _clock };
        _suspensions = SuspensionStore.For(_locations);
        _config = HostConfig.Defaults(_locations);
        string project = PlanSnapshotHashes.ProjectKey(Path.Combine(_locations.DefaultScratch, "Demo"));
        var change = new PlannedChange { Action = "create", Kind = "page", Name = "+A/1" };
        _fresh = new() { Project = "Demo", ProjectPath = project, Fingerprint = "temporary-session-fingerprint", Changes = [change] };
        var scope = new PlanScope([], [], []);
        JsonElement args = PlanTestData.Json("""{"pages":[{"name":"+A/1","page_type":"Circuit","description":""}],"language":"en_US"}""");
        var step = new PlanStep(0, "page.create", args, ToolTier.Write, scope, new(["+A/1"], [], []),
            [new PlanMembers(scope, PlanSnapshotHashes.Members(project, []), 0), new PlanAbsent("page", "+A/1")], [],
            [new(change.Action, change.Kind, change.Name, null, null, null)], [], null, true);
        _definition = PlanTestData.Definition() with
        {
            Project = new("Demo", project, "Demo@1"),
            Owner = _tasks.OwnerFor(_server),
            Resources = [],
            Sources = [],
            Steps = [step],
            Inputs = new([new(step.Op, args)]),
            RequiredOperations = [OperationNames.ProjectSnapshot, OperationNames.DeviceList, OperationNames.ProjectBackup, OperationNames.PageCreatePlan, OperationNames.PageCreateExecute],
            Digests = new(PlanSnapshotHashes.Compose([]), PlanSnapshotHashes.Members(project, []), PlanSnapshotHashes.Names(project, []), null),
        };
        _bridge.Status = new([new("fake-only", Environment.ProcessId)], new()
        {
            ProtocolVersion = 1,
            Eplan = new() { ProcessId = Environment.ProcessId, Version = _definition.EplanVersion, Variant = _definition.EplanVariant },
            Operations = [.. _definition.RequiredOperations],
        }, null);
        _bridge.Handlers[OperationNames.ProjectSnapshot] = _ => Snapshot();
        _bridge.Handlers[OperationNames.DeviceList] = _ => new ListData<DeviceListItem> { Project = "Demo" };
        _bridge.Handlers[OperationNames.PageCreatePlan] = _ => _fresh;
        _bridge.Handlers[OperationNames.ProjectBackup] = a =>
        {
            ProjectBackupArgs request = Assert.IsType<ProjectBackupArgs>(a);
            Assert.Equal(60, request.Target!.MaxSeconds);
            _backupPath = request.Target.Path;
            File.WriteAllText(_backupPath, "fake complete copy");
            return new ProjectBackupData { Path = _backupPath, Pages = 0, Seconds = 0.5m };
        };
    }

    public async ValueTask DisposeAsync()
    {
        _ready?.Dispose();
        _tasks.Dispose();
        await _server.DisposeAsync();
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public async Task ScratchWrite_AutoAllows_BacksUpOnce_AndHandsOffWithoutExecuting()
    {
        await Publish();
        _ready = await Prepare();

        Assert.Equal(PlanStatus.Running, _ready.Record.State.Status);
        Assert.Equal(PolicyVerdict.Allow, _ready.Record.State.Policy);
        Assert.Null(_ready.Record.State.Approval);
        Assert.Equal(TaskStatus.Running, _ready.Task!.Status);
        Assert.Equal([_fresh.Fingerprint], _ready.Fingerprints);
        Assert.Equal(_clock.Now.AddMinutes(15), _ready.Deadline);
        Assert.True(_ready.Backup!.Usable);
        Assert.NotNull(_ready.ProjectLock);
        Assert.Empty(_channel.Questions);
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        Assert.Empty(_bridge.Writes);
        JsonObject approval = Assert.Single(Records(), r => Kind(r) == "plan_approval");
        Assert.Null(approval["decision"]);
        Assert.Equal("Allow", approval["policy"]!.GetValue<string>());
        Assert.Equal("eplan_plan_apply", new PlanApprovalView(_ready.Record, _store.ChangesPath(_definition.PlanId), "folder")
            .Question(_ready.Fingerprints, _ready.Instance!, _ready.Deadline).Tool);
        Assert.Equal((3, null), AuditVerifier.Verify(_locations.Audit));
        Assert.Equal(_ready.Record.State.OwnerHost, approval["ownerHost"]!.GetValue<string>());
        Assert.True(DanglingWrites.IsRunning(approval["host"]));
        Assert.Equal(_ready.Backup, Assert.Single(Records(), r => Kind(r) == "plan_backup")["backup"]!.Deserialize<PlanBackup>(PlanJson.Options));
        Assert.DoesNotContain(_fresh.Fingerprint, await File.ReadAllTextAsync(RecordFile, Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_ExactlyOnce_PersistsBindingAndApprovedThenRunning()
    {
        await Publish(ToolTier.Destructive);
        var statuses = new List<PlanStatus>();
        _channel = new("fake", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                TaskRecord waiting = _tasks.Get(_server, PlanJson.Read<PlanRecord>(File.ReadAllText(RecordFile)).State.TaskId!);
                Assert.Equal(TaskStatus.WaitingApproval, waiting.Status);
                statuses.Add(PlanJson.Read<PlanRecord>(File.ReadAllText(RecordFile)).State.Status);
            },
        };
        _ready = await Prepare();

        Assert.Single(_channel.Questions);
        Assert.Equal([PlanStatus.Compiled], statuses);
        Assert.Equal(3, _ready.Record.State.Revision);
        Assert.Equal(ApprovalDecision.Granted, _ready.Record.State.Approval!.Decision);
        Assert.Equal(_channel.Questions[0].Plan.Fingerprint, _ready.Record.State.Approval.BindingDigest);
        JsonObject approval = Assert.Single(Records(), r => Kind(r) == "plan_approval");
        Assert.Equal(_channel.Questions[0].RequestId, approval["requestId"]!.GetValue<string>());
        Assert.Equal(_channel.Questions[0].ArgumentsHash, approval["argumentsHash"]!.GetValue<string>());
        Assert.Equal("granted", approval["decision"]!.GetValue<string>());
        Assert.Empty(_bridge.Writes);
    }

    [Theory]
    [InlineData((int)ApprovalDecision.Denied, ErrorReason.ApprovalDenied)]
    [InlineData((int)ApprovalDecision.Expired, ErrorReason.ApprovalExpired)]
    [InlineData((int)ApprovalDecision.Unavailable, ErrorReason.ApprovalDenied)]
    public async Task Approval_RefusalConsumesPlan_AndReplayPointsToGet(int decision, string reason)
    {
        await Publish(ToolTier.Destructive);
        _channel = new("fake", (ApprovalDecision)decision);
        using PlanPreparation failed = await Prepare();

        await AssertFailure(failed, reason);
        Assert.Single(_channel.Questions);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        BridgeCallException replay = await Assert.ThrowsAsync<BridgeCallException>(() => Prepare());
        Assert.Equal(ErrorReason.InvalidArgument, replay.Error.Reason);
        Assert.Contains("eplan_plan_get", replay.Error.Message, StringComparison.Ordinal);
        Assert.Single(Records(), r => Kind(r) == "plan_approval");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Suspension_IsASeparateQuestion_BeforePlanApproval(bool release)
    {
        await Publish(ToolTier.Destructive);
        Suspend(_definition.Project.Path, "old-write");
        _channel = new("fake", ApprovalDecision.Granted)
        {
            Answers = q => q.Release is not null && !release ? ApprovalDecision.Denied : ApprovalDecision.Granted,
        };
        _ready = await Prepare();

        Assert.NotNull(_channel.Questions[0].Release);
        Assert.Null(_channel.Questions[0].PlanApproval);
        Assert.Single(Records(), r => Kind(r) == "suspension_release");
        if (release)
        {
            Assert.Equal(2, _channel.Questions.Count);
            Assert.Null(_channel.Questions[1].Release);
            Assert.NotNull(_channel.Questions[1].PlanApproval);
            Assert.Empty(_suspensions.Blocking(_definition.Project.Path, Environment.ProcessId, ProcessStart));
            Assert.Equal(PlanStatus.Running, _ready.Record.State.Status);
        }
        else
        {
            await AssertFailure(_ready, ErrorReason.WritesSuspended);
            Assert.Single(_channel.Questions);
            Assert.DoesNotContain(Records(), r => Kind(r) == "plan_approval");
            Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
            Assert.NotEmpty(_suspensions.Blocking(_definition.Project.Path, Environment.ProcessId, ProcessStart));
        }
    }

    [Fact]
    public async Task ProjectAndInstanceSuspensions_FromDifferentWrites_AreBothConfirmed()
    {
        await Publish(ToolTier.Destructive);
        Suspend(_definition.Project.Path, "project-write");
        Suspend(Path.Combine(_locations.Root, "other"), "instance-write");
        _ready = await Prepare();

        Assert.Equal(["project-write", "instance-write"], _channel.Questions.Where(q => q.Release is not null).Select(q => q.Release!.RequestId));
        Assert.Equal(3, _channel.Questions.Count);
        Assert.Equal(2, Records().Count(r => Kind(r) == "suspension_release"));
    }

    [Fact]
    public async Task BackupFailure_ConsumesPlan_PersistsUnusableBackup_AndSendsZeroSteps()
    {
        await Publish();
        _bridge.Handlers[OperationNames.ProjectBackup] = _ => throw new BridgeCallException(BackupFolders.Failure());
        using PlanPreparation failed = await Prepare();

        await AssertFailure(failed, ErrorReason.BackupFailed);
        Assert.False(Assert.Single(failed.Record.State.Result!.Backups).Usable);
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        Assert.Single(Records(), r => Kind(r) == "plan_result");
    }

    [Fact]
    public async Task UnexpectedFailureAfterAdmission_StillFinishesThePlanAndItsTask()
    {
        await Publish();
        _bridge.Handlers[OperationNames.DeviceList] = _ => throw new InvalidOperationException("unexpected");
        using PlanPreparation failed = await Prepare();

        await AssertFailure(failed, ErrorReason.Internal);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        Assert.Single(Records(), r => Kind(r) == "plan_result");
    }

    [Fact]
    public async Task ConcurrentReplay_WhileApprovalIsOpen_IsRefusedWithoutSecondQuestion()
    {
        await Publish(ToolTier.Destructive);
        _channel = new("fake", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                // Invoke only the synchronous owner/state gate; no blocking waits on asynchronous work.
                PlanRecord admitted = PlanJson.Read<PlanRecord>(File.ReadAllText(RecordFile));
                Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeCallException>(() => PlanOrchestrator.RequireUnused(admitted.State)).Error.Reason);
            },
        };
        _ready = await Prepare();
        BridgeCallException concurrent = await Assert.ThrowsAsync<BridgeCallException>(() => Prepare());

        Assert.Equal(ErrorReason.InvalidArgument, concurrent.Error.Reason);
        Assert.Single(_channel.Questions);
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    [Fact]
    public async Task ConcurrentCalls_WithFirstApprovalPending_OnlyFirstCanConsume()
    {
        await Publish(ToolTier.Destructive);
        var channel = new DeferredChannel();
        _deferred = channel;
        Task<PlanPreparation> first = Prepare();
        ApprovalQuestion question = await channel.Seen.Task.WaitAsync(TimeSpan.FromSeconds(3), Token);
        try
        {
            BridgeCallException replay = await Assert.ThrowsAsync<BridgeCallException>(() => Prepare());
            Assert.Equal(ErrorReason.InvalidArgument, replay.Error.Reason);
            Assert.Contains("eplan_plan_get", replay.Error.Message, StringComparison.Ordinal);
            Assert.Equal(1, channel.Count);
        }
        finally
        {
            channel.Answer.SetResult(new(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, ApprovalDecision.Granted));
            _ready = await first;
        }

        Assert.NotNull(_ready);
        Assert.NotNull(_ready.Record.State.AttemptId);
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    [Fact]
    public async Task DanglingWrite_IsSettledBeforeSuspensionsAndApproval()
    {
        await Publish(ToolTier.Destructive);
        await _audit.AppendAsync(new JsonObject
        {
            ["kind"] = "execution_started",
            ["requestId"] = "unsettled",
            ["project"] = _definition.Project.Path,
            ["tool"] = "previous_tool",
            ["host"] = new JsonObject { ["processId"] = -1 },
            ["eplan"] = new JsonObject
            {
                ["processId"] = Environment.ProcessId,
                ["start"] = ProcessStart.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            },
        }, Token);
        _ready = await Prepare();

        Assert.Equal("unsettled", _channel.Questions[0].Release!.RequestId);
        Assert.NotNull(_channel.Questions[1].PlanApproval);
        string[] kinds = [.. Records().Select(Kind)];
        Assert.True(Array.IndexOf(kinds, "execution_result") < Array.IndexOf(kinds, "suspension_release"));
        Assert.True(Array.IndexOf(kinds, "suspension_release") < Array.IndexOf(kinds, "plan_approval"));
    }

    [Fact]
    public async Task AllIneffectiveSteps_AreConsumedWithoutApprovalOrBackup()
    {
        _fresh.Changes.Clear();
        await Publish(ToolTier.Destructive, d => d with
        {
            Steps = [d.Steps[0] with { Effective = false, PlannedChanges = [] }],
        });
        using PlanPreparation result = await Prepare();

        Assert.Equal(PlanStatus.NotStarted, result.Record.State.Status);
        Assert.NotNull(result.Record.State.AttemptId);
        Assert.Equal(TaskStatus.Succeeded, result.Task!.Status);
        Assert.Empty(_channel.Questions);
        Assert.Empty(result.Record.State.Result!.Backups);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    [Fact]
    public async Task ExhaustedTotalBudget_DoesNotStartBackup()
    {
        await Publish();
        _bridge.Handlers[OperationNames.PageCreatePlan] = _ =>
        {
            _clock.Advance(TimeSpan.FromMinutes(15));
            return _fresh;
        };
        using PlanPreparation result = await Prepare();

        await AssertFailure(result, ErrorReason.Timeout);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResourceHash_IsFreshlyInspectedBeforeAndAfterApproval(bool changesDuringApproval)
    {
        string hash = new('a', 64);
        _bridge.Handlers[OperationNames.PlanMacroInspect] = _ => new PlanMacroInspectData { Sha256 = hash };
        await Publish(ToolTier.Destructive, d => d with
        {
            Sources = [new("macro-example", null, null, null, null, null, null, null, "example.ema", null, hash)],
        });
        if (changesDuringApproval)
        {
            _channel = new("fake", ApprovalDecision.Granted) { Wait = () => hash = new('b', 64) };
        }
        else
        {
            hash = new('b', 64);
        }
        using PlanPreparation result = await Prepare();

        await AssertFailure(result, ErrorReason.PlanStale);
        Assert.Equal(changesDuringApproval ? 1 : 0, _channel.Questions.Count);
        Assert.Equal(changesDuringApproval ? 2 : 1, _bridge.Calls.Count(c => c.Operation == OperationNames.PlanMacroInspect));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    [Fact]
    public async Task TaskCapacityBeforeAdmission_DoesNotConsume()
    {
        await Publish();
        for (int i = 0; i < TaskRegistry.MaxActive; i++)
        {
            _tasks.Create(_server, "other", "OTHER", 1);
        }

        Assert.Equal(ErrorReason.ResourceExhausted, (await Assert.ThrowsAsync<TaskRegistryException>(() => Prepare())).Error.Reason);
        await AssertUnused();
    }

    [Fact]
    public async Task ProjectLockBusy_DoesNotConsume_AndWithdrawsTaskReservation()
    {
        await Publish();
        string key = PlanStore.Hash(Encoding.UTF8.GetBytes(_definition.Project.Path));
        using (var held = new FileStream(Path.Combine(_locations.State, "plans", ".project-" + key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            PlanCapacityException failure = await Assert.ThrowsAsync<PlanCapacityException>(() => Prepare());
            Assert.Equal("plans.project", failure.Metadata["resource"]!.GetValue<string>());
            await AssertUnused();
        }

        _ready = await Prepare();
        Assert.Equal(PlanStatus.Running, _ready.Record.State.Status);
    }

    [Fact]
    public async Task StorageCapacityBeforeAdmission_DoesNotConsume()
    {
        await Publish();
        string filler = Path.Combine(_locations.State, "plans", "capacity.bin");
        using (var file = new FileStream(filler, FileMode.CreateNew))
        {
            file.SetLength(PlanCapacity.Total - PlanCapacity.PerPlan / 2);
        }

        Assert.Equal(ErrorReason.ResourceExhausted, (await Assert.ThrowsAsync<PlanCapacityException>(() => Prepare())).Reason);
        await AssertUnused();
        File.Delete(filler);
        _ready = await Prepare();
    }

    [Fact]
    public async Task DryRun_OnlyRechecksReads_WithoutChangingPlanOrCreatingTask()
    {
        await Publish();
        string before = await File.ReadAllTextAsync(RecordFile, Token);
        using PlanPreparation dry = await Prepare(dryRun: true);

        Assert.Null(dry.Task);
        Assert.Null(dry.ProjectLock);
        Assert.Equal(before, await File.ReadAllTextAsync(RecordFile, Token));
        Assert.Equal(["plan_compiled"], Records().Select(Kind));
        Assert.Empty(_channel.Questions);
        Assert.False(Directory.Exists(_locations.Backups));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation.EndsWith(".execute", StringComparison.Ordinal) || c.Operation == OperationNames.ProjectBackup);
    }

    [Fact]
    public async Task OwnerIsCheckedBeforeExistence_AndBeforeAnyBridgeCall()
    {
        await Publish();
        await using McpServer other = TaskTestData.Server();
        BridgeCallException hidden = await Assert.ThrowsAsync<BridgeCallException>(() => Service().PrepareAsync(other, _definition.PlanId, false, null, Token));
        BridgeCallException missing = await Assert.ThrowsAsync<BridgeCallException>(() => Service().PrepareAsync(other, "../invalid", false, null, Token));

        Assert.Equal(ErrorReason.ResourceNotFound, hidden.Error.Reason);
        Assert.Equal(hidden.Error.Message, missing.Error.Message);
        Assert.Empty(_bridge.Calls);
        await AssertUnused();
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("capability")]
    [InlineData("version")]
    [InlineData("project")]
    [InlineData("guid")]
    [InlineData("names")]
    [InlineData("semantics")]
    [InlineData("changes")]
    [InlineData("hash")]
    [InlineData("compiled audit")]
    public async Task InitialConflict_ConsumesPlan_WithoutApprovalOrBackup(string conflict)
    {
        await Publish(ToolTier.Destructive);
        Change(conflict);
        using PlanPreparation failed = await Prepare();

        await AssertFailure(failed, ErrorReason.PlanStale);
        Assert.Empty(_channel.Questions);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("capability")]
    [InlineData("fingerprint")]
    [InlineData("instance")]
    [InlineData("changes")]
    [InlineData("names")]
    [InlineData("pipe")]
    public async Task AfterApprovalConflict_ConsumesPlan_AndAuditsCheckedDecision(string conflict)
    {
        await Publish(ToolTier.Destructive);
        _channel = new("fake", ApprovalDecision.Granted) { Wait = () => Change(conflict) };
        using PlanPreparation failed = await Prepare();

        await AssertFailure(failed, conflict == "expiry" ? ErrorReason.ApprovalExpired : ErrorReason.PlanStale);
        Assert.Single(_channel.Questions);
        Assert.Equal(conflict == "expiry" ? "expired" : "granted", Assert.Single(Records(), r => Kind(r) == "plan_approval")["decision"]!.GetValue<string>());
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    [Fact]
    public async Task ApprovalAuditUnavailable_ConsumesWithoutBackup()
    {
        await Publish(ToolTier.Destructive);
        _channel = new("fake", ApprovalDecision.Granted)
        {
            Wait = () => File.WriteAllText(Path.Combine(_locations.Audit, "2026-10-23.jsonl"), "damaged\n"),
        };
        using PlanPreparation failed = await Prepare();

        await AssertFailure(failed, ErrorReason.AuditUnavailable);
        Assert.Single(_channel.Questions);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    private async Task Publish(ToolTier tier = ToolTier.Write, Func<PlanDefinition, PlanDefinition>? modify = null, Dictionary<string, byte[]>? evidence = null)
    {
        PlanDefinition definition = _definition with { Steps = [_definition.Steps[0] with { Tier = tier }] };
        definition = modify?.Invoke(definition) ?? definition;
        string changes = PlanDiffPreview.For(definition.Steps);
        definition = definition with { Digests = definition.Digests with { ChangesDigest = PlanStore.Hash(Encoding.UTF8.GetBytes(changes)) } };
        await _store.PublishAsync(definition, changes, evidence ?? [], Token);
        _trust.Register(definition);
    }

    private PlanOrchestrator Service()
    {
        var folders = new BackupFolders(_locations, _clock);
        var dangling = new DanglingWrites(_bridge, _suspensions, _audit, _clock, NullLogger<DanglingWrites>.Instance);
        var approvals = new ApprovalService([_deferred ?? _channel], _config, _clock, NullLogger<ApprovalService>.Instance);
        return new(_bridge, new PackLibrary(_locations, _config, _audit, _clock), _store, _trust, _tasks, _suspensions,
            dangling, approvals, _audit, folders, new PlanProjectBackup(_bridge, folders, _clock, NullLogger<PlanProjectBackup>.Instance));
    }

    private Task<PlanPreparation> Prepare(bool dryRun = false) => Service().PrepareAsync(_server, _definition.PlanId, dryRun, null, Token);

    private ProjectSnapshotData Snapshot() => new()
    {
        Project = "Demo",
        ProjectPath = _definition.Project.Path,
        ProjectGuid = _definition.Project.Guid,
        Profile = SnapshotProfiles.StandardName,
        Format = SnapshotProfiles.Format,
        Complete = true,
        SnapshotHash = PlanSnapshotHashes.Compose([]),
    };

    private void Change(string conflict)
    {
        switch (conflict)
        {
            case "expiry":
                _clock.Advance(TimeSpan.FromDays(1));
                break;
            case "capability":
                _bridge.Status.Handshake!.Operations = [];
                break;
            case "version":
                _bridge.Status.Handshake!.Eplan.Version = "other";
                break;
            case "project":
            case "guid":
                _bridge.Handlers[OperationNames.ProjectSnapshot] = _ =>
                {
                    ProjectSnapshotData snapshot = Snapshot();
                    snapshot.ProjectGuid = "other";
                    if (conflict == "project")
                    {
                        snapshot.ProjectPath = Path.Combine(_locations.Root, "copy");
                    }
                    return snapshot;
                };
                break;
            case "names":
                _bridge.Handlers[OperationNames.DeviceList] = _ => new ListData<DeviceListItem>
                {
                    Project = "Demo",
                    Total = 1,
                    Items = [new() { Tag = "+A-K1" }],
                };
                break;
            case "semantics":
                _fresh.Changes[0].Name = "+A/2";
                break;
            case "fingerprint":
                _fresh.Fingerprint = "other-session-fingerprint";
                break;
            case "instance":
                _bridge.Status.Handshake!.Eplan = new()
                {
                    ProcessId = 0,
                    Version = _definition.EplanVersion,
                    Variant = _definition.EplanVariant,
                };
                break;
            case "pipe":
                _bridge.Status = _bridge.Status with { Instances = [new("reloaded-bridge", Environment.ProcessId)] };
                break;
            case "changes":
                File.AppendAllText(_store.ChangesPath(_definition.PlanId), "changed");
                break;
            case "hash":
                PlanRecord record = PlanJson.Read<PlanRecord>(File.ReadAllText(RecordFile));
                PlanDefinition replaced = record.Definition with { EplanVersion = "changed after compilation" };
                File.WriteAllText(RecordFile, JsonSerializer.Serialize(record with { Definition = replaced, PlanHash = PlanCanonical.Hash(replaced) }, PlanJson.Options));
                break;
            case "compiled audit":
                File.Delete(Path.Combine(_locations.Audit, "2026-10-23.jsonl"));
                break;
        }
    }

    private void Suspend(string project, string requestId) => _suspensions.Suspend(project, Environment.ProcessId,
        ProcessStart, "old_tool", requestId, _clock.Now, SuspensionReason.OutcomeUnknown, ["Check earlier changes"]);

    private static DateTimeOffset ProcessStart => SuspensionStore.StartOf(Environment.ProcessId)!.Value;
    private string RecordFile => Path.Combine(_locations.State, "plans", _definition.PlanId + ".json");
    private List<JsonObject> Records() => Directory.GetFiles(_locations.Audit, AuditLog.FilePattern)
        .SelectMany(f => AuditLog.ReadLines(f)).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
    private static string Kind(JsonObject r) => r["kind"]!.GetValue<string>();

    private async Task AssertUnused()
    {
        PlanRecord saved = Assert.IsType<PlanRecord>(await _store.ReadAsync(_definition.PlanId, Token));
        Assert.Null(saved.State.AttemptId);
        Assert.Equal(0, saved.State.Revision);
        Assert.Empty(_channel.Questions);
        Assert.Empty(_bridge.Writes);
    }

    private async Task AssertFailure(PlanPreparation failed, string reason)
    {
        Assert.Equal(PlanStatus.NotStarted, failed.Record.State.Status);
        Assert.Equal(reason, failed.Record.State.Result!.Error!.Reason);
        Assert.NotNull(failed.Record.State.AttemptId);
        Assert.Equal(PlanExecution.NotStarted, failed.Record.State.Result.Execution);
        Assert.All(failed.Record.State.Result.Steps, s => Assert.Equal(ExecutionState.NotStarted, s.Execution));
        Assert.Equal(TaskStatus.Failed, failed.Task!.Status);
        Assert.Null(failed.ProjectLock);
        Assert.Empty(_bridge.Writes);
        PlanRecord saved = Assert.IsType<PlanRecord>(await _store.ReadAsync(_definition.PlanId, Token));
        Assert.Equal(reason, saved.State.Result!.Error!.Reason);
        Assert.Equal(failed.Record.State.AttemptId, saved.State.AttemptId);
    }

    private sealed class DeferredChannel : IApprovalChannel
    {
        public string Name => "deferred";
        public string Where => "in a test window";
        public int Count { get; private set; }
        public TaskCompletionSource<ApprovalQuestion> Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ApprovalAnswer?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
        {
            Count++;
            Seen.SetResult(question);
            return Answer.Task.WaitAsync(cancellationToken);
        }
    }
}
