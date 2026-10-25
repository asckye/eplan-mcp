using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Plans;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

[Trait("Size", "Medium")]
public sealed class PlanApprovalFileTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-plan-review-").FullName);
    private readonly ManualTime _clock = new(PlanTestData.Created.AddMinutes(1));
    private readonly PlanStore _store;
    private PlanRecord _record = PlanApprovalTestData.Record();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public PlanApprovalFileTests()
    {
        _store = new PlanStore(_locations, new AuditLog(_locations.Audit, _clock)) { Clock = _clock };
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TamperingBeforeOrDuringApproval_RefusesThePlan(bool during)
    {
        await Publish();
        void Tamper() => File.WriteAllText(_store.ChangesPath(_record.Definition.PlanId), "replacement");
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                if (during)
                {
                    Tamper();
                }
            },
        };
        if (!during)
        {
            Tamper();
        }

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => Ask(channel));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Equal(during ? 1 : 0, channel.Questions.Count);
    }

    [Fact]
    public async Task CompleteFile_IsReadOnlyAndContainsVisibleEscapes_WithTheBoundDigest()
    {
        _record = PlanApprovalTestData.Record(60, "project\ntext\u202e\u2028");
        await Publish();
        string file = _store.ChangesPath(_record.Definition.PlanId);
        byte[] before = await File.ReadAllBytesAsync(file, Token);
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted);

        ApprovalResult result = await Ask(channel);

        Assert.Equal(ApprovalDecision.Granted, result.Decision);
        Assert.Equal(before, await File.ReadAllBytesAsync(file, Token));
        Assert.Equal(_record.Definition.Digests.ChangesDigest, PlanStore.Hash(before));
        ApprovalQuestion question = Assert.Single(channel.Questions);
        ApprovalAskArgs dialog = ApprovalTexts.For(question, CultureInfo.InvariantCulture);
        Assert.Contains(file, dialog.Summary, StringComparison.Ordinal);
        Assert.Equal(50, dialog.Changes.Count(l => l.StartsWith("    ", StringComparison.Ordinal)));
        Assert.Contains(dialog.Changes, l => l.Contains("\\u000a", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanGrant_DisposesTheV1OutcomeWithoutReportingAnyExecution()
    {
        await Publish();
        using var outcome = new TrackedOutcome();
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted)
        {
            Bind = answer => answer with { Outcome = outcome },
        };

        ApprovalResult result = await Ask(channel);

        Assert.Equal(ApprovalDecision.Granted, result.Decision);
        Assert.Null(result.Outcome);
        Assert.True(outcome.Disposed);
        Assert.False(outcome.Reported);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("fingerprint")]
    [InlineData("arguments")]
    public async Task MismatchedPlanBinding_IsDenied(string mismatch)
    {
        await Publish();
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted)
        {
            Bind = answer => mismatch switch
            {
                "request" => answer with { RequestId = "old" },
                "fingerprint" => answer with { Fingerprint = "old" },
                _ => answer with { ArgumentsHash = "old" },
            },
        };

        Assert.Equal(ApprovalDecision.Denied, (await Ask(channel)).Decision);
        Assert.Single(channel.Questions);
    }

    [Fact]
    public async Task LateGrant_ExpiresUsingTheExistingApprovalDeadline()
    {
        await Publish();
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted)
        {
            Wait = () => _clock.Advance(HostConfig.DefaultApprovalTimeout + TimeSpan.FromSeconds(1)),
        };

        Assert.Equal(ApprovalDecision.Expired, (await Ask(channel)).Decision);
    }

    [Fact]
    public async Task PlanLifetime_BoundsTheQuestionAndIsRecheckedAfterApproval()
    {
        _record = PlanApprovalTestData.WithDigest(_record.Definition with { ExpiresAt = _clock.Now.AddSeconds(1) });
        await Publish();
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted);

        Assert.Equal(ApprovalDecision.Granted, (await Ask(channel)).Decision);
        Assert.Equal(_record.Definition.ExpiresAt, Assert.Single(channel.Questions).Deadline);
        _clock.Advance(TimeSpan.FromSeconds(1));
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => Ask(channel));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Single(channel.Questions);
    }

    [Fact]
    public async Task OfflinePlan_NeverAsksAChannel()
    {
        await Publish();
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted);
        var service = new ApprovalService([channel], new HostConfig([], OutsideScratchWrites.Approve), _clock, NullLogger<ApprovalService>.Instance);

        ApprovalResult result = await service.AskPlanAsync(_store, _record, PlanApprovalTestData.Backup, ["f"], new EplanInstanceInfo { Offline = true }, null, Token);

        Assert.Equal(new ApprovalResult(null, ApprovalDecision.Unavailable), result);
        Assert.Empty(channel.Questions);
    }

    [Fact]
    public async Task ShortRemainingLifetime_UsesTheActualApprovalWindowForProgress()
    {
        _record = PlanApprovalTestData.WithDigest(_record.Definition with { ExpiresAt = _clock.Now.AddSeconds(30) });
        await Publish();
        var reports = new RecordedProgress();
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Denied)
        {
            Wait = () => _clock.Advance(TimeSpan.FromSeconds(15)),
        };
        var service = new ApprovalService([channel], new HostConfig([], OutsideScratchWrites.Approve), _clock, NullLogger<ApprovalService>.Instance);

        await service.AskPlanAsync(_store, _record, PlanApprovalTestData.Backup, ["f"], new EplanInstanceInfo(), reports, Token);

        var progress = Assert.Single(reports.Values);
        Assert.Equal(15, progress.Progress);
        Assert.Equal(30, progress.Total);
    }

    private async Task Publish()
    {
        _record = await _store.PublishAsync(_record.Definition, PlanDiffPreview.For(_record.Definition.Steps), new Dictionary<string, byte[]>(), Token);
    }

    [Fact]
    public async Task SuspensionRelease_AndPlanApproval_AreTwoSeparateQuestions()
    {
        await Publish();
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted);
        var service = new ApprovalService([channel], new HostConfig([], OutsideScratchWrites.Approve), _clock, NullLogger<ApprovalService>.Instance);
        var suspension = new Suspension("PROJECT", null, null, "eplan_page_update", "earlier", _clock.Now, SuspensionReason.OutcomeUnknown, ["previous change"]);

        await service.ConfirmReleaseAsync("release", "eplan_plan_apply", ToolTier.Write, "h", new WritePlan { Fingerprint = "f" }, new EplanInstanceInfo(), suspension, null, Token);
        await service.AskPlanAsync(_store, _record, PlanApprovalTestData.Backup, ["f"], new EplanInstanceInfo(), null, Token);

        Assert.Equal(2, channel.Questions.Count);
        Assert.Same(suspension, channel.Questions[0].Release);
        Assert.Null(channel.Questions[0].PlanApproval);
        Assert.Null(channel.Questions[1].Release);
        Assert.NotNull(channel.Questions[1].PlanApproval);
    }

    private Task<ApprovalResult> Ask(IApprovalChannel channel)
    {
        var service = new ApprovalService([channel], new HostConfig([], OutsideScratchWrites.Approve), _clock, NullLogger<ApprovalService>.Instance);
        return service.AskPlanAsync(_store, _record, PlanApprovalTestData.Backup, ["f"], new EplanInstanceInfo(), null, Token);
    }

    private sealed class TrackedOutcome : IApprovalOutcome
    {
        public bool Disposed { get; private set; }
        public bool Reported { get; private set; }
        public void Dispose() => Disposed = true;
        public Task ReportAsync(string execution, string? reason)
        {
            Reported = true;
            return Task.CompletedTask;
        }
    }
}
