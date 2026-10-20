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
    public async Task AScratchWrite_RunsOnceAndIsAuditedBeforeAndAfter()
    {
        WriteOutcome<JsonObject> outcome = await Run();

        Assert.Equal((ExecutionState.Committed, 1, false), (outcome.Execution, (int)outcome.Data!["created"]!, outcome.Recovered));
        Assert.Single(_bridge.Writes);
        Assert.Equal(["execution_started", "execution_result"], AuditKinds());
        Assert.Equal((2, null), AuditVerifier.Verify(_locations.Audit));
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
    public async Task OutsideScratch_DuringM3a_IsDeniedByPolicy()
    {
        _plan = PlanFor(_realProject);

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_WithApprovalsOn_IsRefusedWhileThereIsNoChannel()
    {
        _plan = PlanFor(_realProject);

        BridgeError error = await Fails(config: new HostConfig([_locations.DefaultScratch], OutsideScratchWrites.Approve));

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
        Assert.Equal(["execution_started", "execution_result", "writes_suspended"], AuditKinds());
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
        var pipeline = new WritePipeline(
            _bridge,
            config ?? HostConfig.Defaults(_locations),
            SuspensionStore.For(_locations),
            new AuditLog(_locations.Audit, TimeProvider.System),
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
