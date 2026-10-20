using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_report_update through the real write pipeline against a fake Bridge (docs/tools/eplan_report_update.md,
/// sections 3, 4, 6 and 7). The sample project has 204 report pages; updating all reports adds +A2&amp;EPA1/1.2 (S11).
/// </summary>
[Trait("Size", "Medium")]
public sealed class ReportUpdateToolTests : IDisposable
{
    private const string NewParts = "+A2&EPA1/1.2";

    private readonly WriteToolHarness _harness = new();

    public ReportUpdateToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.ReportUpdatePlan] = args =>
        {
            var update = (ReportUpdateArgs)args!;
            WritePlan plan = _harness.Plan(update.Pages is null
                ? [new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "reports", OldValue = "204" }]
                : update.Pages.Select(page => new PlannedChange { Action = "update", Kind = "page", Name = page, Property = "report" }));
            plan.Details = new Dictionary<string, string> { ["reportPages"] = "204" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ReportUpdateExecute] = args => new ReportUpdateData
        {
            Project = "Line1",
            Scope = ((ReportUpdateArgs)args!).Pages is null ? ReportUpdateData.ProjectScope : ReportUpdateData.PagesScope,
            Before = 204,
            After = 205,
            AddedCount = 1,
            Added = [NewParts],
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_CountsTheReportPagesNowAndNeverExecutes()
    {
        CallToolResult result = await Update(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, "project", 204), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("scope").GetString(), data.GetProperty("reportPages").GetProperty("before").GetInt32()));
        Assert.All([data.GetProperty("reportPages").GetProperty("after"), data.GetProperty("added"), data.GetProperty("removed"), data.GetProperty("undo")], v => Assert.Equal(JsonValueKind.Null, v.ValueKind));
        Assert.Contains("known only after running", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Dry run: would update all reports (204 report pages now); the pages added or removed are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ADryRunOfGivenPages_NamesHowMany()
    {
        Assert.Equal(
            "Dry run: would update 2 report pages (204 report pages now); the pages added or removed are known only after running. Nothing was changed.",
            WriteToolHarness.Text(await Update(["&AAB1/1", "&AAB1/2"], dryRun: true), 0));
    }

    [Fact]
    public async Task ACommit_ListsTheReportPagesAddedAndRemoved()
    {
        CallToolResult result = await Update();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_report_update", "project", 204, 205), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("scope").GetString(), data.GetProperty("reportPages").GetProperty("before").GetInt32(), data.GetProperty("reportPages").GetProperty("after").GetInt32()));
        Assert.Equal([NewParts], data.GetProperty("added").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal((0, false, JsonValueKind.Null), (data.GetProperty("removed").GetArrayLength(), data.GetProperty("truncated").GetBoolean(), data.GetProperty("hint").ValueKind));
        Assert.Equal("Updated all reports: 1 report page added, 0 removed; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var executed = (ReportUpdateArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.ReportUpdateExecute).Args!;
        Assert.Equal((null, "f1"), (executed.Pages, executed.Fingerprint));
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task GivenPagesWithNothingAddedOrRemoved_StillCommit()
    {
        _harness.Bridge.Handlers[OperationNames.ReportUpdateExecute] = _ => new ReportUpdateData { Project = "Line1", Scope = ReportUpdateData.PagesScope, Before = 204, After = 204 };

        CallToolResult result = await Update(["&AAB1/1", "&AAB1/2"]);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "pages", 0, 0), (data.GetProperty("execution").GetString(), data.GetProperty("scope").GetString(), data.GetProperty("added").GetArrayLength(), data.GetProperty("removed").GetArrayLength()));
        Assert.Equal("Updated 2 report pages: 0 added, 0 removed; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        Assert.Equal(["&AAB1/1", "&AAB1/2"], ((ReportUpdateArgs)_harness.Bridge.Calls.Last().Args!).Pages!);
    }

    [Fact]
    public async Task MoreThan100Changes_AreTruncatedWithTheTotal()
    {
        _harness.Bridge.Handlers[OperationNames.ReportUpdateExecute] = _ => new ReportUpdateData
        {
            Project = "Line1",
            Before = 204,
            After = 214,
            AddedCount = 70,
            RemovedCount = 60,
            Removed = [.. Enumerable.Range(1, 60).Select(i => "&EPA1/" + i)],
            Added = [.. Enumerable.Range(1, 40).Select(i => "&EPA2/" + i)],
        };

        JsonElement data = WriteToolHarness.Structured(await Update());

        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Contains("Listed 100 of 130 added and removed report pages", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecoveredCommit_HasNoPagesAndPointsToPageList()
    {
        _harness.Bridge.WriteRecovered = true;

        CallToolResult result = await Update();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", true, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("recovered").GetBoolean(), data.GetProperty("added").ValueKind));
        Assert.Contains("eplan_page_list", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Updated all reports; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task APageThatIsNotAReportPage_FromThePlan_IsNotStarted()
    {
        _harness.Bridge.Handlers[OperationNames.ReportUpdatePlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Invalid arguments: pages[0] is not a report page; omit 'pages' to update all reports.",
            Metadata = new Dictionary<string, string> { ["violations"] = "pages[0] is not a report page; omit 'pages' to update all reports." },
        });

        JsonElement error = WriteToolHarness.Error(await Update(["=GAA+A1&EFS1/1"]));

        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    public async Task ABridgeError_KeepsItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution };

        JsonElement error = WriteToolHarness.Error(await Update());

        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDenied()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement error = WriteToolHarness.Error(await Update());

        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(" ", 1, "'project' is required")]
    [InlineData("Line1", 0, "'pages' must hold 1 to 50 full page names; omit it for all reports.")]
    [InlineData("Line1", 51, "'pages' must hold 1 to 50 full page names; omit it for all reports.")]
    public async Task HostChecks_FailBeforeTheBridgeIsAsked(string project, int count, string violation)
    {
        string[] pages = [.. Enumerable.Range(1, count).Select(n => "&AAB1/" + n)];

        JsonElement error = WriteToolHarness.Error(await ReportWriteTools.ReportUpdateAsync(_harness.Pipeline, new RecordedProgress(), project, pages, false, TestContext.Current.CancellationToken));

        Assert.Contains(violation, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    [Fact]
    public void TheApprovalLines_SayThatReportPagesMayBeCreatedOrRemoved()
    {
        var all = new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "reports", OldValue = "204" };
        var page = new PlannedChange { Action = "update", Kind = "page", Name = "&AAB1/1", Property = "report" };

        Assert.Equal(
            ["update all reports (204 report pages now); may create, replace or delete report pages", "update report page &AAB1/1"],
            WritePipeline.ChangeLines(new WritePlan { Changes = [all, page] }));
        Assert.Equal("Update all reports (204 report pages now); may create, replace or delete report pages", ApprovalTexts.Change(all, zh: false));
        Assert.Equal("更新全部报表（现有报表页 204 页）；可能新建、替换或删除报表页", ApprovalTexts.Change(all, zh: true));
        Assert.Equal("Update report page &AAB1/1", ApprovalTexts.Change(page, zh: false));
        Assert.Equal("更新报表页 &AAB1/1", ApprovalTexts.Change(page, zh: true));
    }

    private Task<CallToolResult> Update(string[]? pages = null, bool dryRun = false) =>
        ReportWriteTools.ReportUpdateAsync(_harness.Pipeline, new RecordedProgress(), "Line1", pages, dryRun, TestContext.Current.CancellationToken);
}
