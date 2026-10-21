using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Localization;
using EplanMcp.Workbench.Status;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests.Status;

/// <summary>
/// The status page's logic (M5 design 4.4 item 4, STS-06, ERR-07): doctor's checks in the design's groups by their IDs,
/// unknown IDs under "Other", empty groups left out, write suspensions for the Banner, and the next approval's channel
/// from this Workbench's own state. In the WPF group because the catalogue's language is one per process.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Small")]
public sealed class StatusViewTests(WpfContext wpf)
{
    private const string Report = """
        {"schemaVersion":1,"hostVersion":"0.1.0","status":"warning","checks":[
          {"id":"host.version","status":"info","zh":"eplanmcp 0.1.0","en":"eplanmcp 0.1.0"},
          {"id":"data.directory","status":"ok","zh":"数据目录 D:\\EplanMcp","en":"Data folder D:\\EplanMcp"},
          {"id":"suspension","status":"warning","zh":"写入已暂停：Line1","en":"Writes suspended: Line1"},
          {"id":"eplan.installation","subject":"2026.0.3.25421","status":"ok","zh":"EPLAN 2026","en":"EPLAN 2026"},
          {"id":"future.check","status":"ok","zh":"新的检查\u202E","en":"A new check"},
          {"id":"bridge.licence","subject":"4242","status":"failure","zh":"没有授权","en":"No licence"}
        ]}
        """;

    private readonly FakeHost _host = new();
    private bool _accepting = true;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheChecks_AreGroupedAsTheDesignOrdersThem_UnknownOnesLast_EmptyGroupsLeftOut()
    {
        _host.Answer = _ => new HostRun(1, Report);
        StatusView view = View();

        await view.RunAsync(Token);

        wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            Assert.Equal(
                ["EPLAN 安装与支持矩阵", "授权", "写入暂停", "下一次审批", "数据目录", "环境", "其他检查"],
                view.Groups.Select(g => g.Title));
            StatusLine licence = view.Groups[1].Lines.Single();
            Assert.Equal((CheckStatus.Failure, "失败", "没有授权", "失败：没有授权"), (licence.Status, licence.Severity, licence.Text, licence.AccessibleName));
            Assert.Equal("新的检查", view.Groups[^1].Lines.Single().Text); // the Host's text made plain
            Assert.Equal("eplanmcp 0.1.0：有检查给出警告", view.Summary);
            Loc.Current.Language = AppLanguage.English;
            Assert.Equal("No licence", licence.Text);
        });
        Assert.Equal(["doctor", "--format", "json"], Assert.Single(_host.Runs).Arguments);
    }

    [Fact]
    public async Task AWriteSuspension_IsAlsoForTheBanner_AndAnOkOneIsNot()
    {
        _host.Answer = _ => new HostRun(1, Report);
        StatusView view = View();
        await view.RunAsync(Token);

        wpf.RunWithLanguage(AppLanguage.English, () => Assert.Equal("Writes suspended: Line1", Assert.Single(view.Suspensions).Text));

        _host.Answer = _ => new HostRun(0, Report.Replace("\"status\":\"warning\",\"zh\":\"写入已暂停", "\"status\":\"ok\",\"zh\":\"写入已暂停", StringComparison.Ordinal));
        await view.RunAsync(Token);
        Assert.Empty(view.Suspensions);
    }

    [Theory]
    [InlineData(true, nameof(CheckStatus.Ok), "下一次审批会在这个 Workbench 中询问。")]
    [InlineData(false, nameof(CheckStatus.Warning), "这个 Workbench 现在不接收问题：下一次审批改由 EPLAN 中的对话框询问，没有对话框时拒绝。")]
    public async Task TheNextApproval_GoesToThisWorkbench_OnlyWhileItTakesQuestions(bool accepting, string statusName, string text)
    {
        CheckStatus status = Enum.Parse<CheckStatus>(statusName);
        _accepting = accepting;
        _host.Answer = _ => new HostRun(0, Snapshots.Read("doctor-json"));
        StatusView view = View();

        await view.RunAsync(Token);

        wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            StatusLine line = view.Groups.Single(g => g.Key == "Status.Group.Approvals").Lines.Single();
            Assert.Equal((status, text), (line.Status, line.Text));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ANullCheck_OrAnyFaultWhileRunning_EndsInAnError_NeverStuckRunning(bool nullCheck)
    {
        _host.Answer = _ => nullCheck
            ? new HostRun(0, """{"schemaVersion":1,"hostVersion":"0.1.0","status":"ok","checks":[null]}""")
            : throw new InvalidOperationException("a fault of its own");
        StatusView view = View();

        await view.RunAsync(Token);

        Assert.Equal(StatusState.Failed, view.State);
        Assert.Empty(view.Groups);
        Assert.NotNull(view.Error);
    }

    [Fact]
    public async Task ADoctorThatGivesNoResult_IsAnError_WithItsDetails_AndNoGroups()
    {
        _host.Answer = _ => new HostRun(2, string.Empty);
        StatusView view = View();
        var states = new List<StatusState>();
        view.PropertyChanged += (_, _) => states.Add(view.State);

        await view.RunAsync(Token);

        Assert.Equal([StatusState.Running, StatusState.Failed], states);
        Assert.Empty(view.Groups);
        Assert.Contains("doctor --format json", view.ErrorDetails, StringComparison.Ordinal);
        wpf.RunWithLanguage(AppLanguage.Chinese, () => Assert.StartsWith("eplanmcp.exe 出错退出", view.Error, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryGroupStatusAndOverallStatus_HasItsText()
    {
        string[] keys =
        [
            .. StatusView.Layout.Select(g => g.Key),
            "Status.Group.Other",
            .. Enum.GetNames<CheckStatus>().Select(s => "Status." + s),
            .. Enum.GetNames<CheckStatus>().Select(s => "Status.Overall." + s),
        ];

        Assert.All(keys, k => Assert.True(Catalogue.English.ContainsKey(k), k));
        Assert.False(new StatusView(new HostCommands(null), () => true).CanRun);
    }

    private StatusView View() => new(new HostCommands(_host), () => _accepting);
}
