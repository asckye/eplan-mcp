using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// approval.ask with a fake dialog: what the dialog is given, that the answer repeats the question's binding, the
/// argument checks, a deadline already passed, and the router's EPLAN_BUSY while the main thread cannot show it.
/// </summary>
[Trait("Size", "Small")]
public sealed class ApprovalAskOperationTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeDialog _dialog = new(ApprovalDecisions.Granted);

    [Fact]
    public void Execute_ShowsTheQuestionUntilItsDeadline_AndAnswersWithItsBinding()
    {
        ApprovalAskData data = Run(Question());

        (ApprovalAskArgs shown, TimeSpan timeout) = Assert.Single(_dialog.Asked);
        Assert.Equal(["create page =A1&EFS1/5"], shown.Changes);
        Assert.Equal(TimeSpan.FromMinutes(5), timeout);
        Assert.Equal(("r1", "f1", "h1", ApprovalDecisions.Granted), (data.RequestId, data.Fingerprint, data.ArgumentsHash, data.Decision));
    }

    [Theory]
    [InlineData(ApprovalDecisions.Denied, ApprovalDecisions.Denied)]
    [InlineData(ApprovalDecisions.Cancelled, ApprovalDecisions.Cancelled)]
    [InlineData(ApprovalDecisions.Expired, ApprovalDecisions.Expired)]
    [InlineData("anything else", ApprovalDecisions.Cancelled)]
    public void Execute_PassesTheDialogsDecision_AnUnknownOneAsCancelled(string answered, string decision)
    {
        var dialog = new FakeDialog(answered);

        var data = (ApprovalAskData)new ApprovalAskOperation(dialog, () => _now).Execute(Question())!;

        Assert.Equal(decision, data.Decision);
    }

    [Fact]
    public void Execute_ADeadlineAlreadyPassed_IsExpiredWithoutADialog()
    {
        ApprovalAskArgs question = Question();
        question.DeadlineUnixMs = _now.ToUnixTimeMilliseconds();

        Assert.Equal(ApprovalDecisions.Expired, Run(question).Decision);
        Assert.Empty(_dialog.Asked);
    }

    [Fact]
    public void Execute_AnIncompleteOrOversizedQuestion_IsInvalidAndShowsNothing()
    {
        ApprovalAskArgs[] invalid =
        [
            With(q => q.RequestId = " "),
            With(q => q.Fingerprint = string.Empty),
            With(q => q.ArgumentsHash = string.Empty),
            With(q => q.Title = string.Empty),
            With(q => q.ApproveLabel = string.Empty),
            With(q => q.RejectLabel = string.Empty),
            With(q => q.Changes = []),
            With(q => q.Changes = [.. Enumerable.Repeat("x", ApprovalAskOperation.MaxChanges + 1)]),
            With(q => q.Changes = [new string('x', ApprovalAskOperation.MaxLineLength + 1)]),
            With(q => q.DeadlineUnixMs = (_now + ApprovalAskOperation.MaxTimeout + TimeSpan.FromSeconds(1)).ToUnixTimeMilliseconds()),
        ];

        Assert.All(invalid, q => Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(q)).Error.Reason));
        Assert.Empty(_dialog.Asked);
        Assert.Contains("1-1000 lines of at most 1000", Assert.Throws<BridgeErrorException>(() => Run(With(q => q.Changes = []))).Error.Message, StringComparison.Ordinal);
        Assert.Contains("15 minutes", Assert.Throws<BridgeErrorException>(() => Run(invalid[^1])).Error.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(15), ApprovalAskOperation.MaxTimeout);
    }

    [Fact]
    public void Router_WhileTheMainThreadCannotShowIt_IsEplanBusyAndShowsNothing()
    {
        var router = new OperationRouter([new ApprovalAskOperation(_dialog, () => _now)], new BusyDispatcher(BusyReason.ModalDialog), null, new ListLog(), TimeSpan.FromSeconds(10));

        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.ApprovalAsk, JObject.FromObject(Question(), EplanMcp.Bridge.Core.Protocol.BridgeJson.Serializer), "c"));

        Assert.Equal((ErrorReason.EplanBusy, true), (ex.Error.Reason, ex.Error.Retryable));
        Assert.Empty(_dialog.Asked);
        Assert.Contains(OperationNames.ApprovalAsk, router.SupportedOperations);
    }

    private static ApprovalAskArgs Question() => new()
    {
        RequestId = "r1",
        Fingerprint = "f1",
        ArgumentsHash = "h1",
        DeadlineUnixMs = (_now + TimeSpan.FromMinutes(5)).ToUnixTimeMilliseconds(),
        Title = "Approve?",
        Summary = "eplan_page_create in Line1",
        Changes = ["create page =A1&EFS1/5"],
        Note = "Ctrl+Z",
        ApproveLabel = "Approve",
        RejectLabel = "Reject",
        CountdownFormat = "{0}",
    };

    private static ApprovalAskArgs With(Action<ApprovalAskArgs> change)
    {
        ApprovalAskArgs question = Question();
        change(question);
        return question;
    }

    private ApprovalAskData Run(ApprovalAskArgs question) => (ApprovalAskData)new ApprovalAskOperation(_dialog, () => _now).Execute(question)!;

    private sealed class FakeDialog(string decision) : IApprovalDialog
    {
        public List<(ApprovalAskArgs Question, TimeSpan Timeout)> Asked { get; } = [];

        public string Ask(ApprovalAskArgs question, TimeSpan timeout)
        {
            Asked.Add((question, timeout));
            return decision;
        }
    }
}
