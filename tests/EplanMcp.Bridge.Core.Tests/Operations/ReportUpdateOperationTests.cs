using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// report.update.plan and report.update.execute against a fake project (docs/tools/eplan_report_update.md, sections 2 to
/// 5 and 7). The project has a circuit page and four report pages; updating all reports adds <c>+A2&amp;EPA1/1.2</c>,
/// as S11 found on the sample project (api-notes A16).
/// </summary>
[Trait("Size", "Small")]
public sealed class ReportUpdateOperationTests
{
    private const string Circuit = "=GAA+A1&EFS1/1";
    private const string Contents = "&AAB1/1";
    private const string Terminals1 = "+A2&ETL1/1";
    private const string Terminals2 = "+A2&ETL1/2";
    private const string Parts = "+A2&EPA1/1";
    private const string NewParts = "+A2&EPA1/1.2";

    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _reader = new();
    private readonly FakeReportUpdater _updater = new();
    private readonly FakeTransactions _transactions = new();

    public ReportUpdateOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _reader.Pages.AddRange([.. new[] { Contents, Circuit, Terminals1, Terminals2, Parts }.Select(name => new PageSummary { Name = name })]);
        _updater.Reports = [Report(Contents, "1/1/0"), Report(Terminals1, "1/3/0"), Report(Terminals2, "1/4/0"), Report(Parts, "1/5/0")];
        _updater.AllAfter = [.. _updater.Reports.Take(3), Report(Parts, "1/6/0"), Report(NewParts, "1/7/0")];
        _transactions.Snapshot = () =>
        {
            List<ReportPageRecord> reports = _updater.Reports;
            return () => _updater.Reports = reports;
        };
    }

    [Fact]
    public void Plan_ForAllReports_IsOneUpdateOfTheProjectsReportsWithTheCountNow()
    {
        WritePlan plan = Plan(Args());

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "project", "Line1", "reports", "4", null), (change.Action, change.Kind, change.Name, change.Property, change.OldValue, change.NewValue));
        Assert.Equal((_line1.Name, _line1.Path), (plan.Project, plan.ProjectPath));
        Assert.Equal(new Dictionary<string, string> { ["reportPages"] = "4" }, plan.Details);
        Assert.Empty(_updater.Updates);
    }

    [Fact]
    public void Plan_ForGivenPages_IsOneUpdatePerPageFoundIgnoringCase()
    {
        WritePlan plan = Plan(Args("&aab1/1", Terminals2));

        Assert.Equal(
            [("update", "page", Contents, "report", null), ("update", "page", Terminals2, "report", null)],
            plan.Changes.Select(c => (c.Action, c.Kind, c.Name, c.Property, c.OldValue)));
        Assert.Equal("4", plan.Details!["reportPages"]);
    }

    [Fact]
    public void Plan_PagesThatAreNotReportPages_AreInvalidByTheirIndex()
    {
        _reader.Pages.Add(new PageSummary { Name = "=GAA+A1&EFS1/2" });

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(Circuit, Contents, "=GAA+A1&EFS1/2"))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(
            ["pages[0] is not a report page; omit 'pages' to update all reports.", "pages[2] is not a report page; omit 'pages' to update all reports."],
            error.Metadata!["violations"].Split('\n'));
    }

    [Fact]
    public void Plan_ListsEveryMissingPage()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("&AAB1/9", Contents, "=X/1"))).Error;

        Assert.Equal((ErrorReason.PageNotFound, "&AAB1/9\n=X/1"), (error.Reason, error.Metadata!["missing"]));
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new ReportUpdateArgs { Pages = [Contents, "&aab1/1", string.Empty] };

        string[] violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"].Split('\n');

        Assert.Equal(3, violations.Length);
        Assert.Contains("'project' is required", violations[0], StringComparison.Ordinal);
        Assert.Contains("pages[1] repeats pages[0]", violations[1], StringComparison.Ordinal);
        Assert.Contains("pages[2] is required", violations[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Plan_AnEmptyListOrMoreThan50Pages_IsInvalid(int count)
    {
        var args = new ReportUpdateArgs { Project = "Line1", Pages = [.. Enumerable.Range(1, count).Select(i => "&AAB1/" + i)] };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'pages' must hold 1 to 50 full page names; omit it for the whole project.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AllReports_UpdatesInOneTransactionAndListsTheNewReportPage()
    {
        ReportUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;

        ReportUpdateData data = Execute(args);

        Assert.Equal(("Line1", "project", 4, 5, 1, 0, null), (data.Project, data.Scope, data.Before, data.After, data.AddedCount, data.RemovedCount, data.Warning));
        Assert.Equal([NewParts], data.Added);
        Assert.Empty(data.Removed);
        Assert.Equal([null], _updater.Updates);
        Assert.Equal(["Begin AI: eplan_report_update", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_AgainWithNothingChanged_AddsAndRemovesNothing()
    {
        ReportUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        Execute(args);
        args.Fingerprint = Plan(args).Fingerprint;

        ReportUpdateData data = Execute(args);

        Assert.Equal((5, 5, 0, 0), (data.Before, data.After, data.AddedCount, data.RemovedCount));
        Assert.Empty(data.Added);
    }

    [Fact]
    public void Execute_GivenPages_UpdatesEachInOrderAndReportsTheStepBeforeIt()
    {
        var slot = new ProgressSlot();
        var reported = new List<string?>();
        _updater.OnUpdate = _ => reported.Add(slot.Take()?.Message);
        ReportUpdateArgs args = Args(Terminals2, Contents);
        args.Fingerprint = Plan(args).Fingerprint;

        JToken data = Router().Invoke(OperationNames.ReportUpdateExecute, JObject.FromObject(args, BridgeJson.Serializer), "r1", slot);

        Assert.Equal(("pages", 4, 0), ((string?)data["scope"], (int)data["after"]!, (int)data["addedCount"]!));
        Assert.Equal([Terminals2, Contents], _updater.Updates);
        Assert.Equal(["Updating report page 1 of 2", "Updating report page 2 of 2"], reported);
    }

    [Fact]
    public void Execute_APageThatAnEarlierUpdateRemoved_IsSkippedAndListedAsRemoved()
    {
        _updater.Removes[Terminals1] = [Terminals2];
        ReportUpdateArgs args = Args(Terminals1, Terminals2);
        args.Fingerprint = Plan(args).Fingerprint;

        ReportUpdateData data = Execute(args);

        Assert.Equal([Terminals1, Terminals2], _updater.Updates);
        Assert.Equal((4, 3), (data.Before, data.After));
        Assert.Equal([Terminals2], data.Removed);
    }

    [Fact]
    public void Execute_ListsAtMost100Pages_TheRemovedFirst()
    {
        _updater.Reports = [.. Enumerable.Range(1, 60).Select(i => Report("&EPA1/" + i, "1/" + i + "/0"))];
        _reader.Pages.AddRange(_updater.Reports.Select(r => new PageSummary { Name = r.Name }));
        _updater.AllAfter = [.. Enumerable.Range(1, 70).Select(i => Report("&EPA2/" + i, "2/" + i + "/0"))];
        ReportUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;

        ReportUpdateData data = Execute(args);

        Assert.Equal((70, 40, 60, 60), (data.AddedCount, data.Added.Count, data.RemovedCount, data.Removed.Count));
        Assert.Equal("&EPA1/1", data.Removed[0]);
    }

    [Fact]
    public void Execute_AfterAReportPageWasAddedMeanwhile_IsPlanStaleAndUpdatesNothing()
    {
        ReportUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _updater.Reports = [.. _updater.Reports, Report("+A2&EPA1/2", "1/8/0")]; // the engineer, meanwhile

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_updater.Updates);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_WhenEplanFails_RollsBackTheReportPages()
    {
        ReportUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _updater.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not update the reports.");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal([Contents, Terminals1, Terminals2, Parts], _updater.Reports.Select(r => r.Name));
        Assert.Equal(["Begin AI: eplan_report_update", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutAFingerprint_IsNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args())).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void ACancelDuringTheFirstPage_StopsBeforeTheSecondAndRollsBack()
    {
        _updater.Removes[Terminals1] = [Terminals1];
        ReportUpdateArgs args = Args(Terminals1, Contents);
        args.Fingerprint = Plan(args).Fingerprint;
        OperationRouter router = Router();
        _updater.OnUpdate = _ => router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "r1" }, "cancel");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.ReportUpdateExecute, JObject.FromObject(args, BridgeJson.Serializer), "r1")).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal([Terminals1], _updater.Updates);
        Assert.Contains(_updater.Reports, r => r.Name == Terminals1);
        Assert.Equal(["Begin AI: eplan_report_update", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    private static ReportPageRecord Report(string name, string id) => new() { Name = name, Id = id };

    private static ReportUpdateArgs Args(params string[] pages) => new() { Project = "Line1", Pages = pages.Length == 0 ? null : [.. pages] };

    private OperationRouter Router() =>
        new([new ReportUpdateExecuteOperation(_catalog, _reader, _updater, _transactions)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

    private WritePlan Plan(ReportUpdateArgs args) =>
        (WritePlan)new ReportUpdatePlanOperation(_catalog, _reader, _updater).Execute(args)!;

    private ReportUpdateData Execute(ReportUpdateArgs args) =>
        (ReportUpdateData)new ReportUpdateExecuteOperation(_catalog, _reader, _updater, _transactions).Execute(args)!;
}
