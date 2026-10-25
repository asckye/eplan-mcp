using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Workbench.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>The existing fake Workbench server and real v1 pipe; no EPLAN process or dialog is used.</summary>
[Trait("Size", "Medium")]
public sealed class PlanApprovalTests : IAsyncDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-plan-approval-").FullName);
    private readonly ManualTime _clock = new(PlanTestData.Created.AddMinutes(1));
    private readonly FakeWorkbench _workbench;
    private readonly PlanStore _store;
    private PlanRecord _record = PlanApprovalTestData.Record();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static EplanInstanceInfo Eplan => new() { ProcessId = 42, Version = "2026", Variant = "Pro Panel" };

    public PlanApprovalTests()
    {
        _workbench = new FakeWorkbench(WorkbenchPipeName.ForCurrentUser(_locations.Root));
        _store = new PlanStore(_locations, new AuditLog(_locations.Audit, _clock)) { Clock = _clock };
    }

    public async ValueTask DisposeAsync()
    {
        await _workbench.DisposeAsync();
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Theory]
    [InlineData(WorkbenchDecision.Approved, nameof(ApprovalDecision.Granted))]
    [InlineData(WorkbenchDecision.Rejected, nameof(ApprovalDecision.Denied))]
    public async Task Plan_IsAskedExactlyOnceThroughV1_ThenClosesWithoutAnOutcome(WorkbenchDecision answer, string decision)
    {
        await Publish();
        _workbench.Answer = FakeWorkbench.Decide(answer);
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        ApprovalResult result = await Ask(Service(Channel(), dialog));

        Assert.Equal(new ApprovalResult("workbench", Enum.Parse<ApprovalDecision>(decision)), result);
        Assert.Null(result.Outcome);
        await _workbench.HungUp.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Empty(_workbench.Notifications);
        Assert.Empty(dialog.Questions);
        Assert.Equal(1, _workbench.Connections);
        ApprovalAskParams ask = Assert.Single(_workbench.Asks);
        Assert.Equal((QuestionKind.Approval, QuestionTier.Write, "eplan_plan_apply"), (ask.Kind, ask.Tier, ask.Tool));
        Assert.Equal(PlanApprovalTestData.Backup, ask.BackupFolder);
        Assert.Null(ask.Suspension);
        Assert.Contains(_store.ChangesPath(_record.Definition.PlanId), ask.Changes);
        Assert.Contains(ask.Changes, l => l.Contains(_record.PlanHash, StringComparison.Ordinal));
        Assert.Contains(ask.Changes, l => l.Contains("eplan_plan_get", StringComparison.Ordinal));
        ApprovalQuestion binding = PlanApprovalTestData.View(_record).Question(["f1", "f2"], Eplan, _clock.Now);
        Assert.Equal((binding.Plan.Fingerprint, binding.ArgumentsHash), (ask.Fingerprint, ask.ArgumentsHash));
    }

    [Fact]
    public async Task DeliveredThenDisconnected_NeverSwitchesToTheDialog()
    {
        await Publish();
        _workbench.Answer = (_, _, _) => Task.FromResult<IReadOnlyList<byte[]>?>(null);
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        ApprovalResult result = await Ask(Service(Channel(), dialog));

        Assert.Equal(new ApprovalResult("workbench", ApprovalDecision.Cancelled), result);
        Assert.Single(_workbench.Asks);
        Assert.Empty(dialog.Questions);
    }

    [Fact]
    public async Task APreviousGrant_CannotBeReplayedForTheSameStoredPlan()
    {
        await Publish();
        ApprovalAskResult? earlier = null;
        _workbench.Answer = (ask, id, _) =>
        {
            earlier ??= new(ask.RequestId, ask.Fingerprint, ask.ArgumentsHash, WorkbenchDecision.Approved);
            return Task.FromResult<IReadOnlyList<byte[]>?>([WorkbenchJson.Result(id, earlier)]);
        };
        ApprovalService service = Service(Channel());

        Assert.Equal(ApprovalDecision.Granted, (await Ask(service)).Decision);
        Assert.Equal(ApprovalDecision.Denied, (await Ask(service)).Decision);
        Assert.Equal(2, _workbench.Asks.Count);
        Assert.Equal(2, _workbench.Asks.Select(a => a.RequestId).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(_workbench.Notifications);
    }

    [Fact]
    public async Task ChangesFileReplacedWhileWaiting_IsRehashedAfterTheGrant()
    {
        await Publish();
        _workbench.Answer = async (ask, id, stop) =>
        {
            await File.WriteAllTextAsync(_store.ChangesPath(_record.Definition.PlanId), "different approved text", stop);
            return await FakeWorkbench.Decide(WorkbenchDecision.Approved)(ask, id, stop);
        };

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => Ask(Service(Channel())));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Single(_workbench.Asks);
        await _workbench.HungUp.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Empty(_workbench.Notifications);
    }

    [Fact]
    public async Task ChangesFileTamperedBeforeApproval_IsRefusedWithoutAsking()
    {
        await Publish();
        await File.WriteAllTextAsync(_store.ChangesPath(_record.Definition.PlanId), "tampered", Token);

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => Ask(Service(Channel())));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Empty(_workbench.Asks);
        Assert.Equal(0, _workbench.Connections);
    }

    [Fact]
    public async Task OversizedActualFrame_IsRefusedBeforeDiscoveryAndNeverFallsBack()
    {
        await Publish();
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => Ask(Service(Channel(new string('x', WorkbenchProtocol.MaxFrameBytes)), dialog)));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal(0, _workbench.Connections);
        Assert.Empty(dialog.Questions);
    }

    [Fact]
    public async Task OfflineHost_HasNoApprovalChannel_EvenWithWorkbenchRunning()
    {
        await Publish();
        EplanInstanceInfo eplan = Eplan;
        eplan.Offline = true;
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        ApprovalResult result = await Service(Channel(), dialog).AskPlanAsync(_store, _record, PlanApprovalTestData.Backup, ["f1"], eplan, null, Token);

        Assert.Equal(new ApprovalResult(null, ApprovalDecision.Unavailable), result);
        Assert.Equal(0, _workbench.Connections);
        Assert.Empty(dialog.Questions);
    }

    [Fact]
    public async Task NoWorkbench_UsesExistingDialogFields_AndKeepsSuspensionConfirmationSeparate()
    {
        await Publish();
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        var absent = new WorkbenchChannel(WorkbenchPipeName.ForCurrentUser(Path.Combine(_locations.Root, "absent")), Environment.ProcessPath!, () => "client", _clock, NullLogger.Instance);
        ApprovalService service = Service(absent, dialog);
        var suspension = new Suspension("PROJECT", null, null, "eplan_page_update", "earlier", _clock.Now, SuspensionReason.OutcomeUnknown, ["earlier changes"]);
        var write = new WritePlan { Project = "Demo", ProjectPath = "PROJECT", Fingerprint = "f" };

        await service.ConfirmReleaseAsync("release", "eplan_plan_apply", ToolTier.Write, "h", write, Eplan, suspension, null, Token);
        Assert.Equal(ApprovalDecision.Granted, (await Ask(service)).Decision);

        Assert.Equal(2, dialog.Questions.Count);
        Assert.NotNull(dialog.Questions[0].Release);
        Assert.Null(dialog.Questions[0].PlanApproval);
        Assert.Null(dialog.Questions[1].Release);
        Assert.NotNull(dialog.Questions[1].PlanApproval);
        ApprovalAskArgs args = ApprovalTexts.For(dialog.Questions[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(_store.ChangesPath(_record.Definition.PlanId), args.Summary, StringComparison.Ordinal);
        Assert.Contains(args.Changes, l => l.StartsWith("Step 1:", StringComparison.Ordinal));
    }

    private async Task Publish()
    {
        _record = await _store.PublishAsync(_record.Definition, PlanDiffPreview.For(_record.Definition.Steps), new Dictionary<string, byte[]>(), Token);
    }

    [Fact]
    public async Task GrantAfterTheDeadline_IsExpiredAndNeverSwitchesChannel()
    {
        await Publish();
        _workbench.Answer = (ask, id, stop) =>
        {
            _clock.Advance(HostConfig.DefaultApprovalTimeout + TimeSpan.FromSeconds(1));
            return FakeWorkbench.Decide(WorkbenchDecision.Approved)(ask, id, stop);
        };
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        ApprovalResult result = await Ask(Service(Channel(), dialog));

        Assert.Equal(ApprovalDecision.Expired, result.Decision);
        Assert.Empty(dialog.Questions);
        Assert.Null(result.Outcome);
    }

    private WorkbenchChannel Channel(string client = "client") => new(_workbench.PipeName, Environment.ProcessPath!, () => client, _clock, NullLogger.Instance);

    private ApprovalService Service(params IApprovalChannel[] channels) => new(channels, new HostConfig([], OutsideScratchWrites.Approve), _clock, NullLogger<ApprovalService>.Instance);

    private Task<ApprovalResult> Ask(ApprovalService service) => service.AskPlanAsync(_store, _record, PlanApprovalTestData.Backup, ["f1", "f2"], Eplan, null, Token);
}
