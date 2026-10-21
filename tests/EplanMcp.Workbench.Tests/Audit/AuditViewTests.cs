using System.Text;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Audit;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Localization;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests.Audit;

/// <summary>
/// The audit page's logic (M5 design 4.4 item 2, ERR-05, ERR-06, NAV-10): loading, empty and unreadable told apart, the
/// newest record first, the five filters together, and "verify integrity" through audit verify with what a hash chain
/// cannot show. In the WPF group because the catalogue's language is one per process.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class AuditViewTests(WpfContext wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("eplanmcp-workbench-auditview-").FullName;
    private readonly ManualTime _clock = new(Asks());
    private readonly FakeHost _host = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task ALineWithHalfASurrogatePair_IsNotARecord_AndReadingEnds()
    {
        File.WriteAllText(Path.Combine(_directory, "2026-10-20.jsonl"), "{\"tool\":\"\\ud800\"}\n{\"\\udc00\":1}\n{\"kind\":\"approval\"}\n");
        AuditView view = View();
        int told = 0;
        view.PropertyChanged += (_, e) => told += e.PropertyName == string.Empty ? 1 : 0;

        await view.LoadAsync(Token);

        Assert.Equal((AuditState.Ready, 1, 2), (view.State, view.Total, view.NotRecords));
        Assert.Equal(2, told); // once when reading starts, once when it ends: the page shows twice, not per record or file
    }

    [Fact]
    public async Task AnEmptyLog_IsTheEmptyState_NotAnError()
    {
        AuditView view = View();
        Assert.Equal(AuditState.Loading, view.State);

        await view.LoadAsync(Token);

        Assert.Equal((AuditState.Ready, true, 0), (view.State, view.IsEmpty, view.Total));
    }

    [Fact]
    public async Task AFileThatCannotBeRead_IsAnError_WithWhy()
    {
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "D:\\P1", decision: "granted"));
        using var locked = new FileStream(Path.Combine(_directory, "2026-10-20.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None);
        AuditView view = View();

        await view.LoadAsync(Token);

        Assert.Equal((AuditState.Failed, false), (view.State, view.IsEmpty));
        Assert.NotNull(view.Problem);
    }

    [Fact]
    public async Task TheFilters_AllApplyTogether_NewestFirst_AndEachOffersWhatTheLogHolds()
    {
        Write(
            "2026-10-20",
            Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "D:\\P1", decision: "granted"),
            Line("2026-10-20T12:01:00Z", "execution_result", "eplan_page_create", "D:\\P1", execution: "committed"),
            Line("2026-10-20T13:00:00Z", "approval", "eplan_page_delete", "D:\\P2", decision: "denied"));
        Write("2026-10-21", Line("2026-10-21T12:00:00Z", "approval", "eplan_page_create", "D:\\P2", decision: "granted"));
        AuditView view = View();
        await view.LoadAsync(Token);

        Assert.Equal(["2026-10-21T12:00:00Z", "2026-10-20T13:00:00Z", "2026-10-20T12:01:00Z", "2026-10-20T12:00:00Z"], view.Shown.Select(r => r.Field("time")));
        Assert.Equal([null, "eplan_page_create", "eplan_page_delete"], view.ToolOptions.Select(o => o.Value));
        Assert.Equal([null, "2026-10-21", "2026-10-20"], view.DayOptions.Select(o => o.Value)); // noon UTC: the same day within 11 hours

        view.Tool = "eplan_page_create";
        Assert.Equal(3, view.Shown.Count);
        view.Project = "D:\\P1";
        Assert.Equal(2, view.Shown.Count);
        view.Decision = "granted";
        Assert.Equal("2026-10-20T12:00:00Z", Assert.Single(view.Shown).Field("time"));
        view.Execution = "committed";
        Assert.Empty(view.Shown); // a decision and a result are two records: together they match none

        (view.Decision, view.Execution, view.Project, view.Tool) = (null, null, null, null);
        view.Day = "2026-10-20";
        Assert.Equal(3, view.Shown.Count);
    }

    [Fact]
    public async Task TheDecisionsAndResults_AreOfferedInTheCurrentLanguage_AndAValueTheHostMayAddLater_AsItIs()
    {
        Write(
            "2026-10-20",
            Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "D:\\P1", decision: "denied"),
            Line("2026-10-20T12:01:00Z", "approval", "eplan_page_create", "D:\\P1", decision: "deferred\u202E"));
        AuditView view = View();
        await view.LoadAsync(Token);

        wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Assert.Equal(new FilterOption(null, "全部"), view.DecisionOptions[0]);
            Assert.Equal(["deferred", "已拒绝"], view.DecisionOptions.Skip(1).Select(o => o.Text).Order(StringComparer.Ordinal));
        });
    }

    [Fact]
    public void EveryKind_Decision_ResultAndBreak_TheHostWrites_HasItsText()
    {
        string[] keys =
        [
            .. AuditView.Kinds.Select(k => AuditView.Key("Audit.Kind.", k)),
            .. AuditView.Decisions.Select(d => AuditView.Key("Audit.Decision.", d)),
            .. AuditView.Executions.Select(e => AuditView.Key("Audit.Execution.", e)),
            .. Enum.GetNames<AuditBreakProblem>().Select(p => "Audit.Break." + p),
            .. Enum.GetNames<HostFailure>().Select(f => "Host." + f),
        ];

        Assert.All(keys, k => Assert.True(Catalogue.English.ContainsKey(k), k));
    }

    [Theory]
    [InlineData("intact", "哈希链完整：3 条记录。末尾整段被删除时，后面没有记录可以发现，哈希链本身查不出来。", false)]
    [InlineData("noDirectory", "还没有审计记录：D:\\EplanMcp\\audit 不存在。", false)]
    [InlineData("changed", "哈希链在 2026-10-20.jsonl 第 2 行断开：记录被修改。之前的 1 条记录完整。", true)]
    [InlineData("notARecord", "哈希链在 2026-10-21.jsonl 第 1 行断开：这一行不是记录。之前的 2 条记录完整。", true)]
    public async Task VerifyingIntegrity_RunsAuditVerify_AndSaysWhatItFound(string example, string text, bool failed)
    {
        _host.Answer = _ => new HostRun(failed ? 1 : 0, Snapshots.Example("audit-verify-json", example));
        AuditView view = View();
        bool? busy = null;
        view.PropertyChanged += (_, _) => busy ??= view.IsVerifying;

        await view.VerifyAsync(Token);

        Assert.Equal(["audit", "verify", "--format", "json"], Assert.Single(_host.Runs).Arguments);
        Assert.Equal((failed, false, true), (view.VerificationFailed, view.IsVerifying, busy));
        wpf.RunWithLanguage(AppLanguage.Chinese, () => Assert.Equal(text, view.Verification));
    }

    [Fact]
    public async Task VerifyingIntegrity_WhenTheHostGivesNoResult_SaysWhatToDo()
    {
        _host.Answer = _ => new HostRun(0, "not json");
        AuditView view = View();

        await view.VerifyAsync(Token);

        Assert.True(view.VerificationFailed);
        wpf.RunWithLanguage(AppLanguage.Chinese, () => Assert.Equal("Workbench 读不懂 eplanmcp.exe 返回的结果，两者的版本可能不一致，请重新安装 EplanMcp。", view.Verification));
    }

    private static DateTimeOffset Asks() => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private AuditView View() => new(_directory, new HostCommands(_host), _clock);

    private void Write(string day, params string[] lines) =>
        File.WriteAllText(Path.Combine(_directory, day + ".jsonl"), string.Concat(lines.Select(l => l + "\n")), new UTF8Encoding(false));

    private static string Line(string time, string kind, string tool, string project, string? decision = null, string? execution = null) =>
        $$"""{"time":"{{time}}","kind":"{{kind}}","tool":"{{tool}}","project":"{{project.Replace("\\", "\\\\", StringComparison.Ordinal)}}"{{(decision is null ? "" : $",\"decision\":\"{decision}\"")}}{{(execution is null ? "" : $",\"execution\":\"{execution}\"")}}}""";
}
