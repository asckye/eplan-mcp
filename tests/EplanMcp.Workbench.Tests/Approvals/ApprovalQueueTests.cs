using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// The queue (workbench-ui APR-11 to APR-13, ADR-0014 decision 3): at most 50 waiting questions however many Hosts
/// connect, each question its own whatever its request ID, the first ending wins, and an outcome only ever changes what
/// is shown.
/// </summary>
[Trait("Size", "Small")]
public sealed class ApprovalQueueTests
{
    private readonly ManualTime _clock = new(Asks.Now);

    [Fact]
    public void Fifty_IsTheCapacity_CountingThePlacesHandshakesPromised()
    {
        var queue = new ApprovalQueue(_clock);

        for (int i = 0; i < ApprovalQueue.DefaultCapacity; i++)
        {
            Assert.True(queue.TryReserve());
            if (i % 2 == 0)
            {
                Assert.NotNull(queue.Add(Asks.Write($"r{i}")));
            }
        }

        Assert.Equal(50, ApprovalQueue.DefaultCapacity);
        Assert.False(queue.TryReserve());
        queue.Release();
        Assert.True(queue.TryReserve());
        Assert.False(queue.TryReserve());
    }

    [Fact]
    public void AnEndedQuestion_FreesItsPlace()
    {
        var queue = new ApprovalQueue(_clock, capacity: 1);
        Assert.True(queue.TryReserve());
        Question question = queue.Add(Asks.Write())!;
        Assert.False(queue.TryReserve());

        queue.Answer(question, approve: false);

        Assert.True(queue.TryReserve());
    }

    [Fact]
    public void APlaceThatIsReservedOrWaiting_StopsTakingQuestions_AndEachChangeIsTold()
    {
        var queue = new ApprovalQueue(_clock, capacity: 2);
        var told = new List<bool>();
        queue.AcceptingChanged += () => told.Add(queue.IsAccepting);

        Assert.True(queue.TryReserve());
        Question first = queue.Add(Asks.Write("r1"))!; // the reserved place now waits: one is still free
        Assert.True(queue.TryReserve());
        Assert.False(queue.IsAccepting);
        queue.Release();
        Assert.True(queue.TryReserve());
        queue.Answer(first, approve: true);
        queue.Add(Asks.Write("r2"));
        Assert.True(queue.TryReserve());
        Assert.Null(queue.Add(Asks.Write("r2"))); // refused, yet its place is given back

        Assert.Equal([false, true, false, true, false, true], told);
        Assert.True(queue.IsAccepting);
    }

    [Fact]
    public void AWritesRelease_ThenItsApproval_AreTwoQuestions_UnderOneRequestId()
    {
        var queue = new ApprovalQueue(_clock);
        Question release = queue.Add(Asks.Release("r1"))!;
        Assert.True(queue.Answer(release, approve: true));

        Question approval = queue.Add(Asks.Write("r1"))!;

        Assert.NotSame(release, approval);
        Assert.Equal([approval], queue.Pending);
        Assert.Equal([release], queue.Answered);
        Assert.True(queue.Answer(approval, approve: true));
    }

    [Fact]
    public void TwoReleasesOfDifferentWrites_ThenTheApproval_AreThreeQuestions()
    {
        var queue = new ApprovalQueue(_clock);
        ApprovalAskParams first = Asks.Release("r1");
        ApprovalAskParams second = first with { Suspension = first.Suspension! with { RequestId = "older", Tool = "eplan_device_update" } };

        Question one = queue.Add(first)!;
        Question two = queue.Add(second)!;
        Question three = queue.Add(Asks.Write("r1"))!;

        Assert.Equal([one, two, three], queue.Pending);
    }

    [Fact]
    public void TheVerySameQuestion_AskedAgainWhileItWaits_IsRefused_ButNotOnceItEnded()
    {
        var queue = new ApprovalQueue(_clock);
        Question first = queue.Add(Asks.Release("r1"))!;

        Assert.Null(queue.Add(Asks.Release("r1")));
        Assert.Equal([first], queue.Pending);
        queue.End(first, QuestionEnd.Expired);
        Assert.NotNull(queue.Add(Asks.Release("r1")));
    }

    [Theory]
    [InlineData(true, WorkbenchDecision.Approved, QuestionEnd.Approved)]
    [InlineData(false, WorkbenchDecision.Rejected, QuestionEnd.Rejected)]
    public async Task TheEngineersAnswer_EndsTheQuestion_AndIsWhatTheConnectionSends(bool approve, WorkbenchDecision decision, QuestionEnd end)
    {
        var queue = new ApprovalQueue(_clock);
        Question question = queue.Add(Asks.Write())!;

        Assert.True(queue.Answer(question, approve));

        Assert.Equal(decision, await question.Decision.Task);
        Assert.Equal(end, question.End);
        Assert.Equal(approve ? QuestionExecution.Running : QuestionExecution.None, question.Execution);
        Assert.Empty(queue.Pending);
        Assert.Same(question, Assert.Single(queue.Answered));
    }

    [Theory]
    [InlineData(QuestionEnd.Expired, WorkbenchDecision.Expired)]
    [InlineData(QuestionEnd.Cancelled, WorkbenchDecision.Closed)]
    [InlineData(QuestionEnd.Disconnected, WorkbenchDecision.Closed)]
    [InlineData(QuestionEnd.Closed, WorkbenchDecision.Closed)]
    public async Task AnAnswerAfterTheQuestionEnded_CountsForNothing(QuestionEnd end, WorkbenchDecision decision)
    {
        var queue = new ApprovalQueue(_clock);
        Question question = queue.Add(Asks.Write())!;

        Assert.True(queue.End(question, end));

        Assert.False(queue.Answer(question, approve: true));
        Assert.False(queue.End(question, QuestionEnd.Disconnected));
        Assert.Equal((end, QuestionExecution.None), (question.End, question.Execution));
        Assert.Equal(decision, await question.Decision.Task);
    }

    [Theory]
    [InlineData(QuestionEnd.Approved)]
    [InlineData(QuestionEnd.Rejected)]
    public void OnlyTheEngineer_ApprovesOrRejects(QuestionEnd end)
    {
        var queue = new ApprovalQueue(_clock);
        Question question = queue.Add(Asks.Write())!;

        Assert.False(queue.End(question, end));
        Assert.Null(question.End);
        Assert.False(question.Decision.Task.IsCompleted);
    }

    [Fact]
    public void AConfirmedRelease_HasNoWriteOutcome()
    {
        var queue = new ApprovalQueue(_clock);
        Question question = queue.Add(Asks.Release())!;

        queue.Answer(question, approve: true);
        queue.Report(question, WriteExecution.Committed, null);

        Assert.Equal((QuestionEnd.Approved, QuestionExecution.None), (question.End, question.Execution));
    }

    [Theory]
    [InlineData(WriteExecution.Committed, null, QuestionExecution.Committed)]
    [InlineData(WriteExecution.RolledBack, "EPLAN_OPERATION_FAILED", QuestionExecution.RolledBack)]
    [InlineData(WriteExecution.NotStarted, "PLAN_STALE", QuestionExecution.NotStarted)]
    [InlineData(WriteExecution.Unknown, "OUTCOME_UNKNOWN", QuestionExecution.Unknown)]
    public void TheOutcome_OfAnApprovedWrite_IsShownOnce(WriteExecution execution, string? reason, QuestionExecution shown)
    {
        var queue = new ApprovalQueue(_clock);
        Question question = queue.Add(Asks.Write())!;
        queue.Answer(question, approve: true);

        queue.Report(question, execution, reason);
        queue.Report(question, WriteExecution.Committed, null);
        queue.NoOutcome(question);

        Assert.Equal((QuestionEnd.Approved, shown, reason), (question.End, question.Execution, question.Reason));
    }

    [Fact]
    public void AnOutcome_ForAQuestionThatWasNotApproved_ChangesNothing()
    {
        var queue = new ApprovalQueue(_clock);
        Question waiting = queue.Add(Asks.Write("r1"))!;
        Question rejected = queue.Add(Asks.Write("r2"))!;
        queue.Answer(rejected, approve: false);

        queue.Report(waiting, WriteExecution.Committed, null);
        queue.Report(rejected, WriteExecution.Committed, null);
        queue.NoOutcome(rejected);

        Assert.Equal((null, QuestionExecution.None), (waiting.End, waiting.Execution));
        Assert.Equal(QuestionExecution.None, rejected.Execution);
        Assert.Contains(waiting, queue.Pending);
    }

    [Fact]
    public void AConnectionClosedWithoutAnOutcome_SaysSoAndInfersNothing()
    {
        var queue = new ApprovalQueue(_clock);
        Question question = queue.Add(Asks.Write())!;
        queue.Answer(question, approve: true);

        queue.NoOutcome(question);

        Assert.Equal((QuestionExecution.NotReceived, null), (question.Execution, question.Reason));
    }

    [Fact]
    public void EveryArrivalAndChange_IsAnnounced_InOrder()
    {
        var queue = new ApprovalQueue(_clock);
        var seen = new List<(string, QuestionEnd?, QuestionExecution)>();
        queue.Changed += q => seen.Add((q.RequestId, q.End, q.Execution));

        Question question = queue.Add(Asks.Write())!;
        queue.Answer(question, approve: true);
        queue.Report(question, WriteExecution.Committed, null);

        Assert.Equal([("r1", null, QuestionExecution.None), ("r1", QuestionEnd.Approved, QuestionExecution.Running), ("r1", QuestionEnd.Approved, QuestionExecution.Committed)], seen);
    }

    [Fact]
    public void TheAnsweredGroup_KeepsTheLatest200()
    {
        var queue = new ApprovalQueue(_clock);
        for (int i = 0; i < ApprovalQueue.MaxAnswered + 5; i++)
        {
            queue.End(queue.Add(Asks.Write($"r{i}"))!, QuestionEnd.Expired);
        }

        Assert.Equal(ApprovalQueue.MaxAnswered, queue.Answered.Count);
        Assert.Equal("r5", queue.Answered[0].RequestId);
    }

    [Fact]
    public void AQuestionTheAnsweredGroupLetsGo_IsToldOnce_AndNothingChangesItAfterwards()
    {
        var queue = new ApprovalQueue(_clock);
        Question oldest = queue.Add(Asks.Write("r0"))!;
        queue.Answer(oldest, approve: true);
        var dropped = new List<Question>();
        queue.Changed += q =>
        {
            if (q.Dropped)
            {
                dropped.Add(q);
            }
        };

        for (int i = 1; i <= ApprovalQueue.MaxAnswered; i++)
        {
            queue.End(queue.Add(Asks.Write($"r{i}"))!, QuestionEnd.Expired);
        }

        queue.Report(oldest, WriteExecution.Committed, null);

        Assert.Equal([oldest], dropped);
        Assert.Equal(QuestionExecution.Running, oldest.Execution);
        Assert.DoesNotContain(oldest, queue.Answered);
    }

    [Fact]
    public async Task AnAnswerAndAnEndAtTheSameTime_OneWins_AndTheAnswerSentIsThatOne()
    {
        var queue = new ApprovalQueue(_clock, capacity: 1000);
        Question[] questions = [.. Enumerable.Range(0, 500).Select(i => queue.Add(Asks.Write($"r{i}"))!)];

        bool[][] won = await Task.WhenAll(questions.Select(q => Task.WhenAll(
            Task.Run(() => queue.Answer(q, approve: true), TestContext.Current.CancellationToken),
            Task.Run(() => queue.End(q, QuestionEnd.Expired), TestContext.Current.CancellationToken))));

        for (int i = 0; i < questions.Length; i++)
        {
            Assert.Single(won[i], w => w);
            WorkbenchDecision sent = await questions[i].Decision.Task;
            Assert.Equal(won[i][0] ? (QuestionEnd.Approved, WorkbenchDecision.Approved) : (QuestionEnd.Expired, WorkbenchDecision.Expired), (questions[i].End!.Value, sent));
        }
    }
}
