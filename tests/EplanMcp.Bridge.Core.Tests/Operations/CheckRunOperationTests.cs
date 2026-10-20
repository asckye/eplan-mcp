using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// check.run.plan and check.run.execute against a fake project (docs/tools/eplan_check_run.md, sections 2 to 5 and 7).
/// The project has three pages and no check messages; a check finds 6 warnings and 14 hints, as S6 and S11 found on
/// the sample project (api-notes A11, A16).
/// </summary>
[Trait("Size", "Small")]
public sealed class CheckRunOperationTests
{
    private const string Page1 = "=GAA+A1&EFS1/1";
    private const string Page2 = "=GAA+A1&EFS1/2";

    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _reader = new();
    private readonly FakeCheckRunner _runner = new();
    private readonly FakeTransactions _transactions = new();

    public CheckRunOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        foreach ((string name, string id) in new[] { (Page1, "1/1/0"), (Page2, "1/2/0"), ("&AAB1/1", "1/3/0") })
        {
            _reader.Pages.Add(new PageSummary { Name = name });
            _runner.Pages[name] = id;
        }

        _runner.Found.AddRange([.. Enumerable.Range(1, 6).Select(i => Message(4000 + i, "Warning")), .. Enumerable.Range(1, 14).Select(i => Message(13000 + i, "Hint"))]);
        _transactions.Snapshot = () =>
        {
            List<ProjectMessageRecord> messages = _runner.Messages;
            return () => _runner.Messages = messages;
        };
    }

    [Fact]
    public void Plan_IsOneUpdateOfTheProjectsCheckMessages_WithThePageCountInTheDetails()
    {
        _runner.Messages = [Message(1, "Warning"), Message(2, "Hint", done: true)];

        WritePlan plan = Plan(Args());

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "project", "Line1", "check messages", "2", null), (change.Action, change.Kind, change.Name, change.Property, change.OldValue, change.NewValue));
        Assert.Equal((_line1.Name, _line1.Path), (plan.Project, plan.ProjectPath));
        Assert.Equal(new Dictionary<string, string> { ["pageCount"] = "3" }, plan.Details);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void Plan_OfGivenPages_FindsThemIgnoringCaseAndEchoesTheScheme()
    {
        WritePlan plan = Plan(Args(" Mine ", ["=gaa+a1&efs1/2", Page1]));

        Assert.Equal(new Dictionary<string, string> { ["pageCount"] = "2", ["scheme"] = "Mine" }, plan.Details);
        Assert.Equal("0", Assert.Single(plan.Changes).OldValue);
    }

    [Fact]
    public void Plan_ListsEveryMissingPage()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(null, ["=GAA+A1&EFS1/9", Page1, "=X/1"]))).Error;

        Assert.Equal((ErrorReason.PageNotFound, "=GAA+A1&EFS1/9\n=X/1"), (error.Reason, error.Metadata!["missing"]));
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new CheckRunArgs { Pages = [Page1, "=gaa+a1&efs1/1", string.Empty] };

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
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(null, [.. Enumerable.Range(1, count).Select(i => "=GAA+A1&EFS1/" + i)]))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'pages' must hold 1 to 50 full page names; omit it for the whole project.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChecksInOneTransaction_AndCountsAndListsAsCheckListDoes()
    {
        _runner.Messages = [Message(1, "Error"), Message(2, "Error", done: true)];
        _runner.Found.Insert(0, Message(7001, "Error", done: true));
        CheckRunArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;

        CheckRunData data = Execute(args);

        Assert.Equal(("Line1", 3, null, 2, null), (data.Project, data.PageCount, data.Scheme, data.Before, data.Warning));
        Assert.Equal((20, 0, 6, 14), (data.Total, data.Errors, data.Warnings, data.Hints));
        Assert.Equal(20, data.Messages.Count);
        Assert.Equal((4001, "warning", "Connections", "message 4001"), (data.Messages[0].Number, data.Messages[0].Severity, data.Messages[0].Region, data.Messages[0].Text));
        Assert.Equal((null, string.Empty), Assert.Single(_runner.Runs));
        Assert.Equal(["Begin AI: eplan_check_run", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_ListsAtMost20Messages_AndCountsThemAll()
    {
        _runner.Found.AddRange([.. Enumerable.Range(1, 5).Select(i => Message(7000 + i, "Error"))]);
        CheckRunArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;

        CheckRunData data = Execute(args);

        Assert.Equal((25, 5, 20), (data.Total, data.Errors, data.Messages.Count));
    }

    [Fact]
    public void Execute_OfGivenPages_ChecksThoseWithTheScheme()
    {
        CheckRunArgs args = Args("Mine", ["=gaa+a1&efs1/2", Page1]);
        args.Fingerprint = Plan(args).Fingerprint;

        CheckRunData data = Execute(args);

        Assert.Equal((2, "Mine"), (data.PageCount, data.Scheme));
        (string[]? pages, string scheme) = Assert.Single(_runner.Runs);
        Assert.Equal([Page2, Page1], pages!);
        Assert.Equal("Mine", scheme);
    }

    [Fact]
    public void Execute_AfterACheckRanMeanwhile_IsPlanStaleAndChecksNothing()
    {
        CheckRunArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _runner.Messages = [Message(4001, "Warning")]; // the engineer ran a check meanwhile

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_runner.Runs);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_AfterAPageWasAddedToTheProject_IsPlanStale()
    {
        CheckRunArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _runner.Pages["=GAA+A1&EFS1/60"] = "1/4/0";

        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
    }

    [Fact]
    public void Execute_AfterAPageOutsideTheGivenPagesWasAdded_StillChecks()
    {
        CheckRunArgs args = Args(null, [Page1]);
        args.Fingerprint = Plan(args).Fingerprint;
        _runner.Pages["=GAA+A1&EFS1/60"] = "1/4/0";

        Assert.Equal(1, Execute(args).PageCount);
    }

    [Fact]
    public void Execute_WhenEplanFails_RollsBackAndKeepsTheOldMessages()
    {
        _runner.Messages = [Message(1, "Warning")];
        CheckRunArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _runner.Failure = new BridgeErrorException(ErrorReason.InvalidArgument, "EPLAN has no check scheme 'Mine'.");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal([1], _runner.Messages.Select(m => m.Id));
        Assert.Equal(["Begin AI: eplan_check_run", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutAFingerprint_IsNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args())).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void ACancelWhileTheCheckRuns_RollsItBackWhenItReturns()
    {
        CheckRunArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        var execute = new CheckRunExecuteOperation(_catalog, _reader, _runner, _runner, _transactions);
        var router = new OperationRouter([execute], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));
        _runner.OnRun = () => router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "r1" }, "cancel");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.CheckRunExecute, JObject.FromObject(args, BridgeJson.Serializer), "r1")).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(_runner.Messages);
        Assert.Equal(["Begin AI: eplan_check_run", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    private static ProjectMessageRecord Message(int id, string category, bool done = false) =>
        new() { Id = id, Category = category, Region = "Connections", Text = "message " + id, Done = done };

    private static CheckRunArgs Args(string? scheme = null, string[]? pages = null) => new() { Project = "Line1", Scheme = scheme, Pages = pages?.ToList() };

    private WritePlan Plan(CheckRunArgs args) =>
        (WritePlan)new CheckRunPlanOperation(_catalog, _reader, _runner, _runner).Execute(args)!;

    private CheckRunData Execute(CheckRunArgs args) =>
        (CheckRunData)new CheckRunExecuteOperation(_catalog, _reader, _runner, _runner, _transactions).Execute(args)!;
}
