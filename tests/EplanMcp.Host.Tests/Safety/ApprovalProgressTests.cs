using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// The progress a client sees while the engineer is asked (ADR-0011 section 2): every 15 s until the deadline, where
/// and how long it has waited, and nothing once the answer is in.
/// </summary>
[Trait("Size", "Small")]
public sealed class ApprovalProgressTests
{
    private static readonly WritePlan _plan = new()
    {
        Project = "Line1",
        ProjectPath = @"D:\Projects\Line1",
        Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }],
        Fingerprint = "f1",
    };

    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));
    private readonly RecordedProgress _reports = new();

    [Fact]
    public async Task WhileTheEngineerThinks_EveryFifteenSecondsSaysWhereAndHowLong()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted) { Wait = () => _time.Advance(TimeSpan.FromSeconds(50)) };

        ApprovalResult result = await Ask(dialog);

        Assert.Equal(ApprovalDecision.Granted, result.Decision);
        Assert.Equal([15f, 30f, 45f], _reports.Values.Select(r => r.Progress));
        Assert.All(_reports.Values, r => Assert.Equal(120f, r.Total));
        Assert.Equal("Waiting for the engineer's approval in EPLAN (15 s of 120 s)", _reports.Values[0].Message);
        Assert.Equal("Waiting for the engineer's approval in EPLAN (45 s of 120 s)", _reports.Values[2].Message);
    }

    [Fact]
    public async Task PastTheDeadline_AndAfterTheAnswer_NothingMoreIsReported()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Expired) { Wait = () => _time.Advance(TimeSpan.FromSeconds(135)) };

        await Ask(dialog);
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal([15f, 30f, 45f, 60f, 75f, 90f, 105f, 120f], _reports.Values.Select(r => r.Progress));
    }

    [Fact]
    public async Task TheConfiguredDeadline_IsTheTotal()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Denied) { Wait = () => _time.Advance(TimeSpan.FromSeconds(70)) };

        await Ask(dialog, TimeSpan.FromSeconds(60));

        Assert.Equal([15f, 30f, 45f, 60f], _reports.Values.Select(r => r.Progress));
        Assert.Equal("Waiting for the engineer's approval in EPLAN (60 s of 60 s)", _reports.Values[^1].Message);
    }

    [Fact]
    public async Task TheConfirmationThatLiftsASuspension_SaysWhatIsAsked()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted) { Wait = () => _time.Advance(TimeSpan.FromSeconds(20)) };
        var suspension = new Suspension(_plan.ProjectPath, null, null, "eplan_page_create", "earlier", _time.Now, SuspensionReason.OutcomeUnknown, []);

        await Service(dialog).ConfirmReleaseAsync("r1", "eplan_page_create", "h1", _plan, suspension, _reports, TestContext.Current.CancellationToken);

        Assert.Equal("Waiting for the engineer to confirm in EPLAN that the project was checked (15 s of 120 s)", Assert.Single(_reports.Values).Message);
    }

    [Fact]
    public async Task TheChannelThatAsks_IsTheOneNamed()
    {
        var unable = new FakeApprovalChannel("workbench", null) { Where = "in the Workbench" };
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted) { Wait = () => _time.Advance(TimeSpan.FromSeconds(15)) };

        await Ask(unable, dialog);

        Assert.Contains("in EPLAN", Assert.Single(_reports.Values).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAProgress_NoTimerRuns()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted) { Wait = () => _time.Advance(TimeSpan.FromSeconds(50)) };

        ApprovalResult result = await Service(dialog).AskAsync("r1", "eplan_page_create", "h1", _plan, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalDecision.Granted, result.Decision);
        Assert.Equal(0, _time.TimersCreated);
    }

    [Fact]
    public void TheEplanDialog_IsInEplan() => Assert.Equal("in EPLAN", new EplanDialogChannel(new Tools.FakeBridgeClient(), _time).Where);

    private Task<ApprovalResult> Ask(params IApprovalChannel[] channels) => Ask(channels, HostConfig.DefaultApprovalTimeout);

    private Task<ApprovalResult> Ask(FakeApprovalChannel channel, TimeSpan timeout) => Ask([channel], timeout);

    private Task<ApprovalResult> Ask(IApprovalChannel[] channels, TimeSpan timeout) =>
        Service(channels, timeout).AskAsync("r1", "eplan_page_create", "h1", _plan, _reports, TestContext.Current.CancellationToken);

    private ApprovalService Service(params IApprovalChannel[] channels) => Service(channels, HostConfig.DefaultApprovalTimeout);

    private ApprovalService Service(IApprovalChannel[] channels, TimeSpan timeout) =>
        new(channels, new HostConfig([@"C:\Scratch"], OutsideScratchWrites.Approve) { ApprovalTimeout = timeout }, _time, NullLogger<ApprovalService>.Instance);
}
