using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// The approval queue page's logic (workbench-ui APR-03 to APR-12, T21): earliest deadline first, the shown question kept
/// by request ID, the next one after an answer, no answer before the guard opens or after the question ended, and the
/// ended questions with their results and outcomes. In the WPF group only because the catalogue's language is one
/// per process.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Small")]
public sealed class ApprovalQueueViewTests : IDisposable
{
    private readonly WpfContext _wpf;
    private readonly ManualTime _clock = new(Asks.Now);
    private readonly ApprovalQueue _queue;
    private readonly ApprovalQueueView _view;
    private readonly List<string> _announced = [];
    private int _shown;

    public ApprovalQueueViewTests(WpfContext wpf)
    {
        _wpf = wpf;
        _queue = new ApprovalQueue(_clock);
        _queue.Changed += q => _view!.Update(q);
        _view = new ApprovalQueueView(_queue, _clock);
        _view.Announced += _announced.Add;
        _view.ShownChanged += () => _shown++;
    }

    public void Dispose() => _view.Dispose();

    [Fact]
    public void TheEarliestDeadline_IsOnTop_AndEqualDeadlinesKeepTheirOrderOfArrival()
    {
        Add("late", 90);
        Add("soon", 30);
        Add("mid", 60);
        Add("mid-later", 60);

        Assert.Equal(["soon", "mid", "mid-later", "late"], _view.Pending.Select(i => i.RequestId));
    }

    [Fact]
    public void ANewQuestion_NeverReplacesTheOneShown_OnlyAnEmptyQueueShowsIt()
    {
        Assert.True(_view.IsEmpty);

        Add("first", 90);
        Add("sooner", 30);

        Assert.Equal("first", _view.Shown!.RequestId);
        Assert.Equal(1, _shown);
        Assert.False(_view.IsEmpty);
    }

    [Fact]
    public void NoAnswer_BeforeTheGuardOpens()
    {
        Add("r1", 90);

        Assert.False(_view.Answer(approve: true));
        _view.Guard.Rendered();
        _clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.False(_view.Answer(approve: true));
        _clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.True(_view.CanAnswer);
        Assert.True(_view.Answer(approve: true));
        Assert.Equal(QuestionEnd.Approved, _queue.Answered.Single().End);
    }

    [Fact]
    public void AfterAnAnswer_TheNextQuestionShows_AndTheGuardStartsOver()
    {
        Add("r1", 90);
        Add("r2", 60);
        Open();

        Assert.True(_view.Answer(approve: false));

        Assert.Equal("r2", _view.Shown!.RequestId);
        Assert.False(_view.CanAnswer);
        Assert.False(_view.Answer(approve: true)); // the second click of a double click lands nowhere
        Assert.Equal([QuestionEnd.Rejected], _queue.Answered.Select(q => q.End!.Value));
    }

    [Theory]
    [InlineData(QuestionEnd.Expired, "已过期")]
    [InlineData(QuestionEnd.Cancelled, "已取消：客户端取消了调用")]
    [InlineData(QuestionEnd.Disconnected, "已取消：连接断开")]
    [InlineData(QuestionEnd.Closed, "已取消：Workbench 退出")]
    public void AShownQuestionThatEnds_StaysShownWithItsReason_AndCannotBeAnswered(QuestionEnd end, string result)
    {
        _wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Question question = Add("r1", 90);
            Add("r2", 120);
            Open();

            _queue.End(question, end);

            Assert.Equal(("r1", false, result), (_view.Shown!.RequestId, _view.Shown.IsPending, _view.Shown.Result));
            Assert.False(_view.CanAnswer);
            Assert.False(_view.Answer(approve: true));
            Assert.Contains(result, _announced);
            Assert.Equal("r2", Assert.Single(_view.Pending).RequestId);
        });
    }

    [Fact]
    public void AnEndedQuestion_CanBeLookedAt_ButNotAnswered()
    {
        Question question = Add("r1", 90);
        _queue.End(question, QuestionEnd.Expired);
        Add("r2", 90);

        _view.Select(_view.Answered.Single());
        Open();

        Assert.False(_view.CanAnswer);
        Assert.False(_view.Answer(approve: true));
    }

    [Fact]
    public void TheAnsweredGroup_IsLatestFirst_AndShowsWhatBecameOfAnApprovedWrite()
    {
        _wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Question first = Add("r1", 90);
            Question second = Add("r2", 90);
            _queue.Answer(first, approve: true);
            _queue.Answer(second, approve: false);
            QuestionItem approved = _view.Answered.Single(i => i.RequestId == "r1");
            Assert.Equal(["r2", "r1"], _view.Answered.Select(i => i.RequestId));
            Assert.Equal(("已批准", "执行中"), (approved.Result, approved.Outcome));

            _queue.Report(first, WriteExecution.Unknown, "OUTCOME_UNKNOWN");

            Assert.Equal("结果未知：写入已暂停，下一次写入前会请你确认（OUTCOME_UNKNOWN）", approved.Outcome);
            Assert.True(approved.OutcomeIsWarning);
            Assert.Contains(approved.Outcome, _announced);
            Assert.Equal(("已拒绝", string.Empty), (_view.Answered[0].Result, _view.Answered[0].Outcome));
        });
    }

    [Fact]
    public void TheShownQuestionsThirtyAndTenSeconds_AreAnnounced_AndTheLast20AreAWarning()
    {
        _wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Add("r1", 35);
            _view.Tick();

            _clock.Advance(TimeSpan.FromSeconds(6));
            _view.Tick();
            Assert.False(_view.Shown!.IsWarning);
            _clock.Advance(TimeSpan.FromSeconds(10));
            _view.Tick();
            Assert.True(_view.Shown.IsWarning);
            _clock.Advance(TimeSpan.FromSeconds(10));
            _view.Tick();

            Assert.Equal(["还剩 30 秒", "还剩 10 秒"], _announced.Where(a => a.StartsWith("还剩", StringComparison.Ordinal)));
            Assert.Equal("0:09 后自动拒绝", _view.Shown.CountdownText);
        });
    }

    [Fact]
    public void AnApproval_ReadsAsTheEplanDialog_WithTheClientUnverified()
    {
        _wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Add("r1", 120, Asks.Write() with { ClientName = null, Changes = ["新建页 =A1&EFS1/5\u202E", "", "多行\r\n文字"] });
            QuestionItem item = _view.Shown!;

            Assert.Equal(("批准 AI 修改 EPLAN 项目？", "批准", "拒绝", "2:00 后自动拒绝"), (item.Title, item.ApproveLabel, item.RejectLabel, item.CountdownText));
            Assert.Equal(("未提供", "写入", string.Empty), (item.Client, item.Tier, item.Backup));
            Assert.Equal("2026.0.3.25421 · Pro Panel · 进程 4242", item.Eplan);
            Assert.Equal(string.Join(Environment.NewLine, "新建页 =A1&EFS1/5", string.Empty, "多行 ⏎ 文字"), item.Changes);
            Assert.Equal("批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: eplan_page_create”这一步。", item.Note);
            Assert.Equal("eplan_page_create，写入，项目 Line1，还剩 2:00", item.AccessibleName);
            Loc.Current.Language = AppLanguage.English;
            Assert.Equal(("Approve AI changes to the EPLAN project?", "Rejected automatically in 2:00", "Not given"), (item.Title, item.CountdownText, item.Client));
        });
    }

    [Fact]
    public void ADestructiveWrite_NamesItsBackupFolder()
    {
        _wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Add("r1", 120, Asks.Write(tier: QuestionTier.Destructive, tool: "eplan_page_delete"));
            Add("r2", 120, Asks.Write("r2", tier: QuestionTier.Destructive) with { BackupFolder = null });

            Assert.Equal((true, "破坏性", @"执行前先备份到 D:\EplanMcp\backups\Line1"), (_view.Pending[0].IsDestructive, _view.Pending[0].Tier, _view.Pending[0].Backup));
            Assert.Equal("Host 没有给出备份目录。", _view.Pending[1].Backup);
        });
    }

    [Fact]
    public void AReleaseQuestion_AsksToCheckFirst_WithoutCtrlZ_AndShowsTheSuspendedWrite()
    {
        _wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Add("r1", 120, Asks.Release());
            QuestionItem item = _view.Shown!;

            Assert.Equal(("上次 AI 修改的结果未知", "已核实，继续", "保持暂停", "2:00 后自动保持暂停"), (item.Title, item.ApproveLabel, item.RejectLabel, item.CountdownText));
            Assert.DoesNotContain("Ctrl+Z", item.Note, StringComparison.Ordinal);
            Assert.EndsWith(@" · eplan_page_delete · 项目 D:\Projects\Line1", item.Suspension, StringComparison.Ordinal);
            _queue.Answer(item.Question, approve: true);
            Assert.Equal(("已继续", string.Empty), (item.Result, item.Outcome));
        });
    }

    [Fact]
    public void ALongChangeList_SaysHowManyLinesItLeftOut()
    {
        _wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Add("r1", 120, Asks.Write() with { Changes = [.. Enumerable.Range(0, ProjectText.MaxLines + 3).Select(i => $"line {i}")] });

            Assert.EndsWith("……还有 3 行", _view.Shown!.Changes, StringComparison.Ordinal);
        });
    }

    private Question Add(string requestId, int seconds, ApprovalAskParams? ask = null)
    {
        ApprovalAskParams question = (ask ?? Asks.Write(requestId)) with { RequestId = requestId, DeadlineUnixMs = (_clock.GetUtcNow() + TimeSpan.FromSeconds(seconds)).ToUnixTimeMilliseconds() };
        return _queue.Add(question)!;
    }

    private void Open()
    {
        _view.Guard.Rendered();
        _clock.Advance(ResponseGuard.Delay);
    }
}
