using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// The write pipeline against a fake Bridge, with real files for the audit and the suspensions (M3 design 3, ADR-0005,
/// ADR-0010). The test process stands in for the running EPLAN, so its start time identifies the instance.
/// </summary>
[Trait("Size", "Medium")]
public sealed class WritePipelineTests : IDisposable
{
    private const string Tool = "eplan_page_create";
    private const string Plan = "page.create.plan";
    private const string Execute = "page.create.execute";

    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-pipeline-").FullName);
    private readonly FakeBridgeClient _bridge = new();
    private readonly string _scratchProject;
    private readonly string _realProject;
    private WritePlan _plan;

    public WritePipelineTests()
    {
        _scratchProject = Path.Combine(_locations.DefaultScratch, "Line1");
        _realProject = Path.Combine(_locations.Root, "Projects", "Line1");
        _plan = PlanFor(_scratchProject);
        _bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", Environment.ProcessId)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3", Variant = "Electric P8" } },
            null);
        _bridge.Handlers[Plan] = _ => _plan;
        _bridge.Handlers[Execute] = _ => new JsonObject { ["created"] = 1 };
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public void HashArguments_DependsOnEveryValue()
    {
        Assert.Equal(WritePipeline.HashArguments(new { a = 1, b = "x" }), WritePipeline.HashArguments(new { a = 1, b = "x" }));
        Assert.NotEqual(WritePipeline.HashArguments(new { a = 1, b = "x" }), WritePipeline.HashArguments(new { a = 1, b = "y" }));
        Assert.Matches("^[0-9a-f]{64}$", WritePipeline.HashArguments(new { a = 1 }));
    }

    [Fact]
    public async Task AScratchWrite_RunsOnceAndIsAuditedBeforeAndAfter()
    {
        WriteOutcome<JsonObject> outcome = await Run();

        Assert.Equal((ExecutionState.Committed, 1, false), (outcome.Execution, (int)outcome.Data!["created"]!, outcome.Recovered));
        Assert.Single(_bridge.Writes);
        Assert.Equal(["execution_started", "execution_result"], AuditKinds());
        Assert.Equal((2, null), AuditVerifier.Verify(_locations.Audit));
    }

    [Fact]
    public async Task AWriteACrashedHostLeftUnknown_SuspendsTheProjectBeforeThisOne()
    {
        string eplanStart = SuspensionStore.StartOf(Environment.ProcessId)!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await new AuditLog(_locations.Audit, TimeProvider.System).AppendAsync(
            new JsonObject
            {
                ["kind"] = "execution_started",
                ["requestId"] = "old",
                ["tool"] = Tool,
                ["project"] = _scratchProject,
                ["eplan"] = new JsonObject { ["processId"] = Environment.ProcessId, ["start"] = eplanStart },
                ["host"] = new JsonObject { ["processId"] = Environment.ProcessId, ["start"] = "2000-01-01T00:00:00.0000000+00:00" },
            },
            TestContext.Current.CancellationToken);

        BridgeError error = await Fails();

        Assert.Equal(ErrorReason.WritesSuspended, error.Reason);
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task TheStartedRecord_NamesTheHostAndTheEplanInstance()
    {
        await Run();

        JsonObject started = AuditRecords()[0];
        Assert.Equal(Environment.ProcessId, started["host"]!["processId"]!.GetValue<int>());
        Assert.NotNull(started["eplan"]!["start"]);
        Assert.Empty(new AuditLog(_locations.Audit, TimeProvider.System).Dangling());
    }

    [Fact]
    public async Task ADryRunOrAnEmptyPlan_ReturnsThePlanAndChangesNothing()
    {
        WriteOutcome<JsonObject> dry = await Run(dryRun: true);
        _plan = new WritePlan { Project = "Line1", ProjectPath = _scratchProject };
        WriteOutcome<JsonObject> empty = await Run();

        Assert.Equal((ExecutionState.NotStarted, 1), (dry.Execution, dry.Plan.Changes.Count));
        Assert.Equal(ExecutionState.NotStarted, empty.Execution);
        Assert.Empty(_bridge.Writes);
        Assert.False(Directory.Exists(_locations.Audit));
    }

    [Fact]
    public async Task OutsideScratch_WhenConfigDeniesIt_IsDeniedByPolicy()
    {
        _plan = PlanFor(_realProject);

        BridgeError error = await Fails(config: new HostConfig([_locations.DefaultScratch], OutsideScratchWrites.Deny));

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_ByDefault_NeedsApprovalAndIsRefusedWhileThereIsNoChannel()
    {
        _plan = PlanFor(_realProject);

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task AnUnknownOutcome_SuspendsTheProjectAndTheNextWriteIsRefused()
    {
        _bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Message = "m", Execution = ExecutionState.Unknown };

        BridgeError unknown = await Fails();
        _bridge.WriteFailure = null;
        BridgeError next = await Fails();

        Assert.Equal(ExecutionState.Unknown, unknown.Execution);
        Assert.Equal((ErrorReason.WritesSuspended, ExecutionState.NotStarted), (next.Reason, next.Execution));
        Assert.Single(_bridge.Writes); // the second never reached the Bridge
        Assert.Equal(2, SuspensionStore.For(_locations).All().Count);
        Assert.Equal(["execution_started", "execution_result", "writes_suspended", "suspension_release"], AuditKinds()); // no channel: kept
    }

    [Fact]
    public async Task ARolledBackWrite_IsAuditedAndNotSuspended()
    {
        _bridge.WriteFailure = new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "m", Execution = ExecutionState.RolledBack };

        BridgeError error = await Fails();

        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Empty(SuspensionStore.For(_locations).All());
        Assert.Equal("rolled_back", AuditRecords()[1]["execution"]!.GetValue<string>());
    }

    [Fact]
    public async Task ARecoveredCommit_IsReportedAsSuch()
    {
        _bridge.WriteRecovered = true;

        WriteOutcome<JsonObject> outcome = await Run();

        Assert.Equal((ExecutionState.Committed, true, null), (outcome.Execution, outcome.Recovered, outcome.Data));
        Assert.True(AuditRecords()[1]["recovered"]!.GetValue<bool>());
    }

    [Fact]
    public async Task WithoutAnAuditLog_NothingIsWritten()
    {
        Directory.CreateDirectory(_locations.Root);
        File.WriteAllText(_locations.Audit, "a file where the audit directory should be");

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.AuditUnavailable, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task APlanThatFails_IsNotStarted()
    {
        _bridge.Failure = new BridgeError { Reason = ErrorReason.ProjectNotOpen, Message = "m" };

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.ProjectNotOpen, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    private static WritePlan PlanFor(string projectPath) => new()
    {
        Project = "Line1",
        ProjectPath = projectPath,
        Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }],
        Fingerprint = "f",
    };

    private Task<WriteOutcome<JsonObject>> Run(bool dryRun = false, HostConfig? config = null)
    {
        var audit = new AuditLog(_locations.Audit, TimeProvider.System);
        config ??= HostConfig.Defaults(_locations);
        var pipeline = new WritePipeline(
            _bridge,
            config,
            SuspensionStore.For(_locations),
            audit,
            new DanglingWrites(_bridge, SuspensionStore.For(_locations), audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService([], config, TimeProvider.System, NullLogger<ApprovalService>.Instance),
            new BackupFolders(_locations, TimeProvider.System),
            TimeProvider.System,
            NullLogger<WritePipeline>.Instance);
        var request = new WriteRequest<object, object>(Tool, ToolTier.Write, Plan, Execute, new { pages = 1 }, plan => new { plan.Fingerprint }, dryRun, TimeSpan.FromSeconds(60));
        return pipeline.RunAsync<object, object, JsonObject>(request, TestContext.Current.CancellationToken);
    }

    private async Task<BridgeError> Fails(HostConfig? config = null)
    {
        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Run(config: config))).Error;
        Assert.True(ExecutionInvariants.Hold(error), $"{error.Reason}/{error.Execution}/{error.Retryable}");
        return error;
    }

    private List<JsonObject> AuditRecords() =>
        [.. Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];

    private string[] AuditKinds() => [.. AuditRecords().Select(r => r["kind"]!.GetValue<string>())];
}
