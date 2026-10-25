using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanSettlementTests : IDisposable
{
    private readonly PlanStoreFixture _fixture = new();
    private readonly FakeBridgeClient _bridge = new();
    private readonly AuditLog _audit;
    private readonly PlanStore _store;
    private readonly SuspensionStore _suspensions;
    private PlanRecord _record = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public PlanSettlementTests()
    {
        _audit = new(_fixture.Locations.Audit, TimeProvider.System) { LockWait = TimeSpan.Zero };
        _store = new(_fixture.Locations, _audit);
        _suspensions = SuspensionStore.For(_fixture.Locations);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Restart_CommittedAndPendingStep_FoldsLateCommitAndRetainsBackup()
    {
        await Accepted();
        await Started(1);
        await Ended(1, ExecutionState.Committed);
        await Started(2);
        _bridge.Outcomes[Request(2)] = new() { Status = OutcomeStatus.Committed };
        await _audit.AppendAsync(new JsonObject
        {
            ["kind"] = "plan_backup",
            ["planId"] = Id,
            ["backup"] = JsonSerializer.SerializeToNode(new PlanBackup("retained-copy", 5, 0.5m, true), PlanJson.Options),
        }, Token);

        await New().StartAsync(Token);

        PlanRecord completed = await Read();
        Assert.Equal(PlanStatus.Committed, completed.State.Status);
        Assert.Equal([false, true, false], completed.State.Result!.Steps.Select(s => s.Recovered));
        Assert.Equal(new PlanBackup("retained-copy", 5, 0.5m, true), Assert.Single(completed.State.Result.Backups));
        Assert.Equal("skipped", completed.State.Result.Verification);
        Assert.Empty(_suspensions.All());
        Assert.Single(Results());
        Assert.True(AuditVerifier.Verify(_fixture.Locations.Audit).Break is null);
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task StartedAfterDanglingPass_RemainsPending_UntilNextPassSettlesDelayedCommit()
    {
        await Accepted();
        DanglingWrites earlier = Dangling();
        await earlier.SettleOnceAsync(Token);
        await Started(1);
        await Ended(1, ExecutionState.Committed);
        await Started(2);
        PlanSettlement settlement = New(earlier);

        await settlement.SettleAsync(Token);

        Assert.Equal(PlanStatus.Running, (await Read()).State.Status);
        Assert.Empty(Results());

        _bridge.Outcomes[Request(2)] = new() { Status = OutcomeStatus.Committed };
        await Dangling().SettleOnceAsync(Token);
        await settlement.SettleAsync(Token);

        PlanResult result = (await Read()).State.Result!;
        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.True(result.Steps[1].Recovered);
    }

    [Theory]
    [InlineData(OutcomeStatus.Expired)]
    [InlineData(OutcomeStatus.Running)]
    public async Task UnrecoverableOutcome_IsUnknown_AndSuspensionIsNeverCleared(string outcome)
    {
        await Accepted();
        await Started(1);
        _bridge.Outcomes[Request(1)] = new() { Status = outcome };

        PlanSettlement settlement = New();
        await settlement.SettleAsync(Token);
        Suspension[] suspended = [.. _suspensions.All()];
        await settlement.SettleAsync(Token);

        PlanResult result = (await Read()).State.Result!;
        Assert.Equal(PlanExecution.Unknown, result.Execution);
        Assert.Equal(ErrorReason.OutcomeUnknown, result.Steps[0].Error!.Reason);
        Assert.False(result.Steps[0].Recovered);
        Assert.Equal(ExecutionState.NotStarted, result.Steps[1].Execution);
        Assert.Equal(2, suspended.Length);
        Assert.Equivalent(suspended, _suspensions.All(), strict: true);
        Assert.Single(Results());
        Assert.False(result.Retryable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LivingOwner_WithLostTaskAndFreshRegistry_IsNeverTakenOver(bool started)
    {
        await Accepted(living: true);
        if (started)
        {
            await Started(1, living: true);
        }
        using var freshTasks = new TaskRegistry(TimeProvider.System);
        await using var server = TaskTestData.Server();
        Assert.NotEqual(_record.State.OwnerHost, freshTasks.OwnerFor(server).RunId);

        await New().SettleAsync(Token);

        Assert.Equal(_record.State, (await Read()).State);
        Assert.Empty(Results());
        Assert.Empty(_suspensions.All());
        TaskRegistryException missing = Assert.Throws<TaskRegistryException>(() => freshTasks.Get(server, _record.State.TaskId!));
        Assert.Equal(ErrorReason.ResourceNotFound, missing.Error.Reason);
    }

    [Fact]
    public async Task DeadOwner_LostTask_SettlesWithoutRestoringIdentityOrTrust()
    {
        await Accepted();
        await Started(1);
        await Ended(1, ExecutionState.Committed);
        using var freshTasks = new TaskRegistry(TimeProvider.System);
        await using var server = TaskTestData.Server();

        await New().SettleAsync(Token);

        PlanRecord completed = await Read();
        Assert.Equal(PlanStatus.PartiallyCommitted, completed.State.Status);
        Assert.Equal(_record.State.TaskId, completed.State.TaskId);
        Assert.Equal(_record.State.OwnerHost, completed.State.OwnerHost);
        Assert.Equal(_record.State.AttemptId, completed.State.AttemptId);
        Assert.False(new PlanTrust().Owns(Id, freshTasks.OwnerFor(server)));
        Assert.Throws<TaskRegistryException>(() => freshTasks.Get(server, completed.State.TaskId!));
    }

    [Fact]
    public async Task ApprovedNeverStarted_CompletesNotStarted_WithoutReapprovalOrWrites()
    {
        await Accepted(approved: true);
        await New().SettleAsync(Token);

        PlanRecord completed = await Read();
        Assert.Equal(PlanStatus.NotStarted, completed.State.Status);
        Assert.Equal(_record.State.Approval, completed.State.Approval);
        Assert.All(completed.State.Result!.Steps, s => Assert.Equal(ExecutionState.NotStarted, s.Execution));
        Assert.Single(Records(), e => Kind(e) == "plan_approval");
        Assert.Empty(_bridge.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentHosts_PublishOneResult_AndOneTerminalRevision(bool pending)
    {
        await Accepted();
        await Started(1);
        await Ended(1, ExecutionState.Committed);
        if (pending)
        {
            await Started(2);
            _bridge.Outcomes[Request(2)] = new() { Status = OutcomeStatus.Committed };
        }

        await Task.WhenAll(Task.Run(() => New().SettleAsync(Token), Token), Task.Run(() => New().SettleAsync(Token), Token));
        await New().SettleAsync(Token);

        Assert.Equal(_record.State.Revision + 1, (await Read()).State.Revision);
        Assert.Equal(pending ? PlanStatus.Committed : PlanStatus.PartiallyCommitted, (await Read()).State.Status);
        Assert.Single(Results());
        Assert.True(AuditVerifier.Verify(_fixture.Locations.Audit).Break is null);
    }

    [Fact]
    public async Task CrashAfterAudit_BeforeState_RetriesWithoutDuplicateResult()
    {
        await Accepted(approved: true);
        var failing = new PlanStore(_fixture.Locations, _audit)
        {
            Checkpoint = point =>
            {
                if (point == "replacement_staged")
                {
                    throw new IOException("Simulated crash after result publication.");
                }
            },
        };
        await new PlanSettlement(failing, _audit, _fixture.Locations, TimeProvider.System, Dangling()).SettleAsync(Token);
        Assert.Equal(_record.State, (await Read()).State);
        JsonObject published = Assert.Single(Results());

        using (var unavailable = new FileStream(Assert.Single(Directory.GetFiles(_fixture.Locations.Audit, AuditLog.FilePattern)),
            FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await New().SettleAsync(Token);
            Assert.Equal(_record.State, (await Read()).State);
        }

        await New().SettleAsync(Token);

        Assert.Equal(PlanStatus.NotStarted, (await Read()).State.Status);
        Assert.Equal(published.ToJsonString(), Assert.Single(Results()).ToJsonString());
    }

    [Theory]
    [InlineData(FileShare.None)]
    [InlineData(FileShare.Read)]
    public async Task AuditUnavailableForReadingOrAppending_WritesNothing_ThenRetries(FileShare sharing)
    {
        await Accepted(approved: true);
        string file = Assert.Single(Directory.GetFiles(_fixture.Locations.Audit, AuditLog.FilePattern));
        string before = await File.ReadAllTextAsync(file, Token);
        PlanSettlement settlement = New();
        using (var unavailable = new FileStream(file, FileMode.Open, FileAccess.Read, sharing))
        {
            await settlement.SettleAsync(Token);
            Assert.Equal(_record.State, (await Read()).State);
        }
        Assert.Equal(before, await File.ReadAllTextAsync(file, Token));

        await settlement.SettleAsync(Token);

        Assert.Equal(PlanStatus.NotStarted, (await Read()).State.Status);
        Assert.Single(Results());
    }

    [Fact]
    public async Task OwnersOwnResult_IsUsedAsIs_WithoutASecondResult()
    {
        await Accepted();
        await Started(1);
        await Ended(1, ExecutionState.Committed);
        await Started(2);
        await Ended(2, ExecutionState.Committed);
        // The owner audited its result with verification, then exited before writing the state.
        PlanResult owners = PlanSettlement.Fold(_record, Records())! with { Verification = "passed" };
        await _audit.AppendAsync(new JsonObject
        {
            ["kind"] = "plan_result",
            ["planId"] = Id,
            ["result"] = JsonSerializer.SerializeToNode(owners, PlanJson.Options),
        }, Token);

        await New().SettleAsync(Token);

        PlanRecord completed = await Read();
        Assert.Equal(PlanStatus.Committed, completed.State.Status);
        Assert.Equal("passed", completed.State.Result!.Verification);
        Assert.Single(Results());
    }

    [Fact]
    public async Task OtherRecordShapes_DoNotStopSettlement()
    {
        await Accepted(approved: true);
        await _audit.AppendAsync(new JsonObject { ["kind"] = "other", ["planId"] = 5, ["step"] = "first" }, Token);

        await New().SettleAsync(Token);

        Assert.Equal(PlanStatus.NotStarted, (await Read()).State.Status);
    }

    [Fact]
    public async Task MissingProcessIdentity_DoesNotGuessThatOwnerExited()
    {
        await Accepted(approved: true);
        // A different stored run ID with no audit mapping is not proof of process death.
        PlanRecord changed = _record with { State = _record.State with { OwnerHost = "unmapped-owner" } };
        await File.WriteAllTextAsync(_fixture.Record, JsonSerializer.Serialize(changed, PlanJson.Options), Token);

        await New().SettleAsync(Token);

        Assert.Equal(changed.State, (await Read()).State);
        Assert.Empty(Results());
    }

    [Fact]
    public async Task IncompleteAudit_WritesNothing_AndLeavesAcceptedPlanPending()
    {
        await Accepted(approved: true);
        string file = Assert.Single(Directory.GetFiles(_fixture.Locations.Audit, AuditLog.FilePattern));
        await File.AppendAllTextAsync(file, "{\"kind\":", Token);
        string before = await File.ReadAllTextAsync(file, Token);

        await New().SettleAsync(Token);

        Assert.Equal(_record.State, (await Read()).State);
        Assert.Equal(before, await File.ReadAllTextAsync(file, Token));
    }

    private string Id => _record.Definition.PlanId;
    private async Task<PlanRecord> Read() => (await _store.ReadAsync(Id, Token))!;
    private List<JsonObject> Records() => [.. Directory.GetFiles(_fixture.Locations.Audit, AuditLog.FilePattern).Order(StringComparer.Ordinal)
        .SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];
    private JsonObject[] Results() => [.. Records().Where(e => Kind(e) == "plan_result")];
    private static string Kind(JsonObject entry) => entry["kind"]!.GetValue<string>();
    private static string Request(int index) => "request-" + index.ToString(CultureInfo.InvariantCulture);
    private DanglingWrites Dangling() => new(_bridge, _suspensions, _audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance);
    private PlanSettlement New(DanglingWrites? dangling = null) => new(_store, _audit, _fixture.Locations, TimeProvider.System, dangling ?? Dangling());

    private async Task Accepted(bool living = false, bool approved = false)
    {
        PlanDefinition definition = PlanSettlementFoldTests.Record().Definition with
        {
            Project = new("Demo", Path.Combine(_fixture.Locations.DefaultScratch, "Demo"), null),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Digests = _fixture.Definition.Digests,
        };
        _record = await _store.PublishAsync(definition, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token);
        _record = (await _store.UpdateAsync(Id, _record.State.Revision, s => s.Admit("accepted", "lost-task", definition.Owner.RunId), Token))!;
        await _audit.AppendAsync(new JsonObject
        {
            ["kind"] = "plan_approval",
            ["planId"] = Id,
            ["ownerHost"] = definition.Owner.RunId,
            ["host"] = Host(living),
        }, Token);
        if (approved)
        {
            _record = (await _store.UpdateAsync(Id, _record.State.Revision, s => s.Approve(new(ApprovalDecision.Granted, "test", "binding")), Token))!;
        }
        else
        {
            _record = (await _store.UpdateAsync(Id, _record.State.Revision, s => s.Start(DateTimeOffset.UtcNow, PolicyVerdict.Allow), Token))!;
        }
    }

    private Task<string> Started(int index, bool living = false)
    {
        JsonObject entry = PlanSettlementFoldTests.Entry("execution_started", index);
        entry["project"] = _record.Definition.Project.Path;
        entry["tool"] = "eplan_plan_apply";
        entry["host"] = Host(living);
        entry["eplan"] = DanglingWrites.ThisHost();
        return _audit.AppendAsync(entry, Token);
    }

    private Task<string> Ended(int index, string execution)
    {
        JsonObject entry = PlanSettlementFoldTests.Entry("execution_result", index);
        entry["execution"] = execution;
        return _audit.AppendAsync(entry, Token);
    }

    private static JsonObject Host(bool living)
    {
        JsonObject host = DanglingWrites.ThisHost();
        if (!living)
        {
            host["start"] = DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture);
        }
        return host;
    }
}
