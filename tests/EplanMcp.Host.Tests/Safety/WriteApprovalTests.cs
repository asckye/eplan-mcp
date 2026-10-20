using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// The approval step of the write pipeline with fake channels (ADR-0005 section 3, M3 design 4.3): every decision, the
/// order of the channels, answers that do not belong to the question or come too late, and the audit of each decision
/// as its own record linked by the Host's request ID. Real files for the audit and the suspensions.
/// </summary>
[Trait("Size", "Medium")]
public sealed class WriteApprovalTests : IDisposable
{
    private const string Tool = "eplan_page_create";
    private const string Plan = "page.create.plan";
    private const string Execute = "page.create.execute";

    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-approval-").FullName);
    private readonly FakeBridgeClient _bridge = new();
    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);
    private readonly string _realProject;

    public WriteApprovalTests()
    {
        _realProject = Path.Combine(_locations.Root, "Projects", "Line1");
        _bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", Environment.ProcessId)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3", Variant = "Electric P8" } },
            null);
        _bridge.Handlers[Plan] = _ => PlanFor(_realProject);
        _bridge.Handlers[Execute] = _ => new JsonObject { ["created"] = 1 };
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task Granted_TheWriteRunsOnceAndTheGrantIsRecordedFirst()
    {
        var dialog = new FakeChannel("eplan_dialog", ApprovalDecision.Granted);

        WriteOutcome<JsonObject> outcome = await Run(dialog);

        Assert.Equal(ExecutionState.Committed, outcome.Execution);
        Assert.Single(_bridge.Writes);
        ApprovalQuestion question = dialog.Questions.Single();
        Assert.Equal((Tool, "f1"), (question.Tool, question.Plan.Fingerprint));
        Assert.Equal(["create page =A1&EFS1/5"], question.Changes);
        Assert.Equal(_bridge.Writes[0].RequestId, question.RequestId);
        Assert.Equal(_clock.Now + ApprovalService.Timeout, question.Deadline);
        List<JsonObject> records = AuditRecords();
        Assert.Equal(["approval", "execution_started", "execution_result"], records.Select(r => Text(r, "kind")));
        Assert.Equal(("eplan_dialog", "granted", "f1"), (Text(records[0], "channel"), Text(records[0], "decision"), Text(records[0], "fingerprint")));
        Assert.Equal("create page =A1&EFS1/5", records[0]["changes"]![0]!.GetValue<string>());
        Assert.All(records, r => Assert.Equal(question.RequestId, Text(r, "requestId")));
        Assert.Equal((3, null), AuditVerifier.Verify(_locations.Audit));
    }

    [Theory]
    [InlineData(nameof(ApprovalDecision.Denied), ErrorReason.ApprovalDenied, "denied")]
    [InlineData(nameof(ApprovalDecision.Cancelled), ErrorReason.ApprovalDenied, "cancelled")]
    [InlineData(nameof(ApprovalDecision.Expired), ErrorReason.ApprovalExpired, "expired")]
    public async Task NotGranted_NothingRunsAndTheDecisionIsRecorded(string decision, string reason, string recorded)
    {
        BridgeError error = await Fails(new FakeChannel("eplan_dialog", Enum.Parse<ApprovalDecision>(decision)));

        Assert.Equal((reason, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Empty(_bridge.Writes);
        JsonObject record = Assert.Single(AuditRecords());
        Assert.Equal(("approval", recorded), (Text(record, "kind"), Text(record, "decision")));
    }

    [Fact]
    public async Task WithoutAChannelThatCanAsk_TheWriteIsRefusedAsUnavailable()
    {
        var unable = new FakeChannel("eplan_dialog", null);

        BridgeError none = await Fails();
        BridgeError cannot = await Fails(unable);

        Assert.All([none, cannot], e => Assert.Equal((ErrorReason.ApprovalDenied, ApprovalService.NoChannel), (e.Reason, e.Message)));
        Assert.Single(unable.Questions);
        Assert.Empty(_bridge.Writes);
        Assert.All(AuditRecords(), r => Assert.Equal((null, "unavailable"), (r["channel"]?.GetValue<string>(), Text(r, "decision"))));
    }

    [Fact]
    public async Task AChannelThatCannotAsk_PassesTheQuestionToTheNext()
    {
        var first = new FakeChannel("workbench", null);
        var second = new FakeChannel("eplan_dialog", ApprovalDecision.Granted);

        await Run(first, second);

        Assert.Equal((1, 1), (first.Questions.Count, second.Questions.Count));
        Assert.Equal("eplan_dialog", Text(AuditRecords()[0], "channel"));
    }

    [Fact]
    public async Task AnAnswerForAnotherRequestPlanOrArguments_IsDenied()
    {
        BridgeError otherRequest = await Fails(new FakeChannel("eplan_dialog", ApprovalDecision.Granted) { Bind = a => a with { RequestId = "other" } });
        BridgeError otherPlan = await Fails(new FakeChannel("eplan_dialog", ApprovalDecision.Granted) { Bind = a => a with { Fingerprint = "f0" } });
        BridgeError otherArguments = await Fails(new FakeChannel("eplan_dialog", ApprovalDecision.Granted) { Bind = a => a with { ArgumentsHash = "h" } });

        Assert.All([otherRequest, otherPlan, otherArguments], e => Assert.Equal(ErrorReason.ApprovalDenied, e.Reason));
        Assert.Empty(_bridge.Writes);
        Assert.All(AuditRecords(), r => Assert.Equal("denied", Text(r, "decision")));
    }

    [Fact]
    public async Task AGrantAfterTheDeadline_HasExpired()
    {
        var late = new FakeChannel("eplan_dialog", ApprovalDecision.Granted) { Wait = () => _clock.Now += ApprovalService.Timeout + TimeSpan.FromSeconds(1) };

        BridgeError error = await Fails(late);

        Assert.Equal(ErrorReason.ApprovalExpired, error.Reason);
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task AChannelThatFails_ReturnsItsErrorAsNotStarted()
    {
        var busy = new FakeChannel("eplan_dialog", null) { Failure = new BridgeError { Reason = ErrorReason.EplanBusy, Message = "m", Retryable = true } };

        BridgeError error = await Fails(busy);

        Assert.Equal((ErrorReason.EplanBusy, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task AScratchWriteOrADryRun_AsksNobody()
    {
        var dialog = new FakeChannel("eplan_dialog", ApprovalDecision.Denied);

        WriteOutcome<JsonObject> dry = await Run([dialog], dryRun: true);
        _bridge.Handlers[Plan] = _ => PlanFor(Path.Combine(_locations.DefaultScratch, "Line1"));
        await Run(dialog);

        Assert.Equal(ExecutionState.NotStarted, dry.Execution);
        Assert.Empty(dialog.Questions);
        Assert.Single(_bridge.Writes);
    }

    [Fact]
    public async Task ASuspendedProject_IsRefusedBeforeAnyoneIsAsked()
    {
        SuspensionStore.For(_locations).Suspend(_realProject, Environment.ProcessId, SuspensionStore.StartOf(Environment.ProcessId)!.Value, Tool, "r", _clock.Now, SuspensionReason.OutcomeUnknown, []);
        var dialog = new FakeChannel("eplan_dialog", ApprovalDecision.Granted);

        BridgeError error = await Fails(dialog);

        Assert.Equal(ErrorReason.WritesSuspended, error.Reason);
        Assert.Empty(dialog.Questions);
    }

    [Fact]
    public async Task AGrantThatCannotBeAudited_IsNotActedOn_ARefusalStillIsReturned()
    {
        Directory.CreateDirectory(_locations.Root);
        File.WriteAllText(_locations.Audit, "a file where the audit directory should be");

        BridgeError granted = await Fails(new FakeChannel("eplan_dialog", ApprovalDecision.Granted));
        BridgeError denied = await Fails(new FakeChannel("eplan_dialog", ApprovalDecision.Denied));

        Assert.Equal((ErrorReason.AuditUnavailable, ErrorReason.ApprovalDenied), (granted.Reason, denied.Reason));
        Assert.Empty(_bridge.Writes);
    }

    private static string Text(JsonObject record, string name) => record[name]!.GetValue<string>();

    private static WritePlan PlanFor(string projectPath) => new()
    {
        Project = "Line1",
        ProjectPath = projectPath,
        Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }],
        Fingerprint = "f1",
    };

    private Task<WriteOutcome<JsonObject>> Run(params IApprovalChannel[] channels) => Run(channels, dryRun: false);

    private Task<WriteOutcome<JsonObject>> Run(IApprovalChannel[] channels, bool dryRun)
    {
        var audit = new AuditLog(_locations.Audit, TimeProvider.System);
        var pipeline = new WritePipeline(
            _bridge,
            new HostConfig([_locations.DefaultScratch], OutsideScratchWrites.Approve),
            SuspensionStore.For(_locations),
            audit,
            new DanglingWrites(_bridge, SuspensionStore.For(_locations), audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService(channels, _clock, NullLogger<ApprovalService>.Instance),
            TimeProvider.System,
            NullLogger<WritePipeline>.Instance);
        var request = new WriteRequest<object, object>(Tool, ToolTier.Write, Plan, Execute, new { pages = 1 }, plan => new { plan.Fingerprint }, dryRun, TimeSpan.FromSeconds(60));
        return pipeline.RunAsync<object, object, JsonObject>(request, TestContext.Current.CancellationToken);
    }

    private async Task<BridgeError> Fails(params IApprovalChannel[] channels)
    {
        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Run(channels))).Error;
        Assert.True(ExecutionInvariants.Hold(error), $"{error.Reason}/{error.Execution}/{error.Retryable}");
        return error;
    }

    private List<JsonObject> AuditRecords() =>
        [.. Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).Order(StringComparer.Ordinal).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];

    /// <summary>
    /// Answers <paramref name="decision"/> for exactly the question asked (null: cannot ask), unless <see cref="Bind"/>
    /// changes the binding; <see cref="Wait"/> runs while "the engineer thinks"; or fails with <see cref="Failure"/>.
    /// </summary>
    private sealed class FakeChannel(string name, ApprovalDecision? decision) : IApprovalChannel
    {
        public List<ApprovalQuestion> Questions { get; } = [];

        public BridgeError? Failure { get; init; }

        public Func<ApprovalAnswer, ApprovalAnswer> Bind { get; init; } = a => a;

        public Action Wait { get; init; } = () => { };

        public string Name => name;

        public Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
        {
            Questions.Add(question);
            if (Failure is not null)
            {
                throw new BridgeCallException(Failure);
            }

            Wait();
            return Task.FromResult(decision is { } d ? Bind(new ApprovalAnswer(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, d)) : null);
        }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
