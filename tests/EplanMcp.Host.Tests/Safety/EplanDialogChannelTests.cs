using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>The EPLAN dialog channel against a fake Bridge: what it sends, how long it waits, what it makes of the answer.</summary>
[Trait("Size", "Small")]
public sealed class EplanDialogChannelTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeBridgeClient _bridge = new();
    private readonly ApprovalQuestion _question;
    private string _decision = ApprovalDecisions.Granted;

    public EplanDialogChannelTests()
    {
        var plan = new WritePlan
        {
            Project = "Line1",
            ProjectPath = @"D:\Projects\Line1",
            Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }],
            Fingerprint = "f1",
        };
        _question = new ApprovalQuestion("r1", "eplan_page_create", "h1", plan, ["create page =A1&EFS1/5"], _now + HostConfig.DefaultApprovalTimeout);
        Connect([OperationNames.ProjectList, OperationNames.ApprovalAsk]);
        _bridge.Handlers[OperationNames.ApprovalAsk] = args =>
        {
            var question = (ApprovalAskArgs)args!;
            return new ApprovalAskData { RequestId = question.RequestId, Fingerprint = question.Fingerprint, ArgumentsHash = question.ArgumentsHash, Decision = _decision };
        };
    }

    [Fact]
    public async Task Ask_SendsTheTextsTheBindingAndTheDeadline_AndWaitsPastIt()
    {
        ApprovalAnswer answer = (await Ask())!;

        var sent = (ApprovalAskArgs)_bridge.LastArgs!;
        ApprovalAskArgs texts = ApprovalTexts.For(_question, CultureInfo.CurrentUICulture);
        Assert.Equal((texts.Title, texts.Summary, texts.Note, texts.ApproveLabel, texts.RejectLabel), (sent.Title, sent.Summary, sent.Note, sent.ApproveLabel, sent.RejectLabel));
        Assert.Equal(texts.Changes, sent.Changes);
        Assert.Equal(("r1", "f1", "h1"), (sent.RequestId, sent.Fingerprint, sent.ArgumentsHash));
        Assert.Equal((_now + HostConfig.DefaultApprovalTimeout).ToUnixTimeMilliseconds(), sent.DeadlineUnixMs);
        Assert.Equal(HostConfig.DefaultApprovalTimeout + EplanDialogChannel.Slack, Assert.Single(_bridge.Timeouts));
        Assert.Equal(new ApprovalAnswer("r1", "f1", "h1", ApprovalDecision.Granted), answer);
    }

    [Theory]
    [InlineData(ApprovalDecisions.Granted, nameof(ApprovalDecision.Granted))]
    [InlineData(ApprovalDecisions.Denied, nameof(ApprovalDecision.Denied))]
    [InlineData(ApprovalDecisions.Cancelled, nameof(ApprovalDecision.Cancelled))]
    [InlineData(ApprovalDecisions.Expired, nameof(ApprovalDecision.Expired))]
    [InlineData("something new", nameof(ApprovalDecision.Cancelled))]
    public async Task Ask_MapsTheDialogsDecision(string answered, string decision)
    {
        _decision = answered;

        Assert.Equal(Enum.Parse<ApprovalDecision>(decision), (await Ask())!.Decision);
    }

    [Fact]
    public async Task Ask_WithAnAddInWithoutTheDialogOrWithoutEplan_CannotAsk()
    {
        Connect([OperationNames.ProjectList, OperationNames.OutcomeGet]);
        ApprovalAnswer? older = await Ask();
        _bridge.Status = new BridgeStatus([], null, BridgeErrors.NotConnected());
        ApprovalAnswer? none = await Ask();

        Assert.Null(older);
        Assert.Null(none);
        Assert.Empty(_bridge.Calls);
    }

    [Fact]
    public async Task Ask_WhenEplanCannotShowIt_FailsWithTheBridgesError()
    {
        _bridge.Failure = new BridgeError { Reason = ErrorReason.EplanBusy, Message = "EPLAN is showing a dialog.", Retryable = true };

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Ask);

        Assert.Equal(ErrorReason.EplanBusy, ex.Error.Reason);
    }

    private Task<ApprovalAnswer?> Ask() => new EplanDialogChannel(_bridge, new FixedClock(_now)).AskAsync(_question, TestContext.Current.CancellationToken);

    private void Connect(List<string> operations) => _bridge.Status = new BridgeStatus(
        [new BridgeEndpoint("p", 1)],
        new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = 1 }, Operations = operations },
        null);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
