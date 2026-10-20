using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// page.create.plan and page.create.execute against a fake project (docs/tools/eplan_page_create.md, sections 4, 5
/// and 7): the checks, the fingerprint (Q6) and the rollback of a failure inside the transaction (Q2).
/// </summary>
[Trait("Size", "Small")]
public sealed class PageCreateOperationTests
{
    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _reader = new();
    private readonly FakePageWriter _writer;
    private readonly FakeTransactions _transactions = new();

    public PageCreateOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _reader.Pages.AddRange([Page("=GAA+A1&EFS1/1"), Page("=GAA+A1&EFS1/2"), Page("=GAB+A1&EFS1/5")]);
        _writer = new FakePageWriter(_reader);
        _transactions.Snapshot = () =>
        {
            PageSummary[] before = [.. _reader.Pages];
            return () =>
            {
                _reader.Pages.Clear();
                _reader.Pages.AddRange(before);
            };
        };
    }

    [Fact]
    public void Plan_ListsOneCreatePerPageWithTypeAndDescription()
    {
        WritePlan plan = Plan(Args(Item("=GAA+A1&EFS1/5", description: "电源"), Item("=GAA+A1&EFS1/6", "overview")));

        Assert.Equal(("Line1", @"D:\Scratch\Line1"), (plan.Project, plan.ProjectPath));
        Assert.Equal(
            [("create", "page", "=GAA+A1&EFS1/5", "Circuit, \"电源\""), ("create", "page", "=GAA+A1&EFS1/6", "Overview")],
            plan.Changes.Select(c => (c.Action, c.Kind, c.Name, c.NewValue)));
        Assert.Empty(_writer.Creates);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Plan_ReportsEveryProblemAtOnce()
    {
        PageCreateArgs args = Args(Item("==GAA/1"), Item("=GAA+A1&EFS1/7", "Schematic"), Item("=gaa+a1&efs1/7"), Item("=X/1", description: new string('x', 501)));
        args.Project = null;
        args.Language = "xx_XX";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        string[] violations = error.Metadata!["violations"].Split('\n');
        Assert.Equal(6, violations.Length);
        Assert.StartsWith("'project' is required", violations[0], StringComparison.Ordinal);
        Assert.StartsWith("pages[0].name uses '=='", violations[1], StringComparison.Ordinal);
        Assert.StartsWith("pages[1].page_type must be one of Circuit, CircuitSingleLine, Overview, Graphics", violations[2], StringComparison.Ordinal);
        Assert.StartsWith("pages[2].name repeats pages[1].name", violations[3], StringComparison.Ordinal);
        Assert.StartsWith("pages[3].description must have at most 500", violations[4], StringComparison.Ordinal);
        Assert.StartsWith("'language' is not a language EPLAN knows", violations[5], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Plan_TakesOneToFiftyPages(int count)
    {
        PageCreateArgs args = Args([.. Enumerable.Range(1, count).Select(n => Item("=GAA+A1&EFS1/" + (100 + n)))]);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Contains("'pages' must hold 1 to 50 pages.", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_PageTypeIgnoresCaseAndDefaultsToCircuit()
    {
        WritePlan plan = Plan(Args(Item("=GAA+A1&EFS1/5", "graphics"), Item("=GAA+A1&EFS1/6", string.Empty)));

        Assert.Equal(["Graphics", "Circuit"], plan.Changes.Select(c => c.NewValue));
    }

    [Fact]
    public void Plan_ANameInUseInAnyCase_IsAlreadyExistsWithTheExistingNames()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(Item("=gaa+A1&EFS1/2"), Item("=GAA+A1&EFS1/9")))).Error;

        Assert.Equal((ErrorReason.AlreadyExists, "=GAA+A1&EFS1/2"), (error.Reason, error.Metadata!["existing"]));
        Assert.Null(error.Execution);
    }

    [Fact]
    public void Plan_AReadOnlyProject_IsRefused()
    {
        _line1.ReadOnly = true;

        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Args(Item("=GAA+A1&EFS1/5")))).Error.Reason);
    }

    [Fact]
    public void Fingerprint_CoversOnlyTheTargetStructuresAndNotTheirOrder()
    {
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/5"));
        string before = Plan(args).Fingerprint;

        _reader.Pages.Add(Page("=GAB+A1&EFS1/6"));
        string elsewhere = Plan(args).Fingerprint;
        _reader.Pages.Reverse();
        string reordered = Plan(args).Fingerprint;
        _reader.Pages.Add(Page("=gaa+a1&efs1/3"));
        string added = Plan(args).Fingerprint;

        Assert.Equal(before, elsewhere);
        Assert.Equal(before, reordered);
        Assert.NotEqual(before, added);
    }

    [Fact]
    public void Execute_CreatesEveryPageInOneCommittedTransaction()
    {
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/5", description: "电源"), Item("=GAA+A1&EFS1/6"));
        args.Language = "zh_CN";
        args.Fingerprint = Plan(args).Fingerprint;

        PageCreateData data = Execute(args);

        Assert.Equal("Line1", data.Project);
        Assert.Equal([("=GAA+A1&EFS1/5", "Circuit", "电源"), ("=GAA+A1&EFS1/6", "Circuit", null)], data.Pages.Select(p => (p.Name, p.Type, p.Description)));
        Assert.Null(data.Warning);
        Assert.Equal(["Begin AI: eplan_page_create", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.All(_writer.Creates, c => Assert.Equal((@"D:\Scratch\Line1", "zh_CN"), (c.Path, c.Language)));
        Assert.Equal(5, _reader.Pages.Count);
    }

    [Fact]
    public void Execute_AfterAPageWasAddedThere_IsPlanStaleAndChangesNothing()
    {
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/5"));
        args.Fingerprint = Plan(args).Fingerprint;
        _reader.Pages.Add(Page("=GAA+A1&EFS1/3")); // the engineer, while the approval waited

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Empty(_writer.Creates);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutAFingerprintOrWithBadArguments_IsNotStarted()
    {
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/5"));
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        args.Pages.Add(Item("bad"));
        args.Fingerprint = "f";
        BridgeError invalid = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (missing.Reason, missing.Execution));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (invalid.Reason, invalid.Execution));
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_ANamePartEplanDrops_RollsBackEveryPage()
    {
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/5"), Item("=GAA+A1&EFS1/6"));
        args.Fingerprint = Plan(args).Fingerprint;
        _writer.Drop = "&EFS1";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(("=GAA+A1&EFS1/5", "=GAA+A1/5"), (error.Metadata!["requested"], error.Metadata["actual"]));
        Assert.Contains("'=GAA+A1/5'", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, _reader.Pages.Count);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_ADescriptionEplanChanged_RollsBack()
    {
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/5", description: "a;b"));
        args.Fingerprint = Plan(args).Fingerprint;
        _writer.ChangeDescription = d => d + " ";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack, "a;b "), (error.Reason, error.Execution, error.Metadata!["actual"]));
        Assert.Equal(3, _reader.Pages.Count);
    }

    [Fact]
    public void Execute_AnEplanFailureOnTheSecondPage_RollsBackTheFirstToo()
    {
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/5"), Item("=GAA+A1&EFS1/6"));
        args.Fingerprint = Plan(args).Fingerprint;
        _writer.FailOn = 2;

        Assert.Throws<InvalidOperationException>(() => Execute(args));

        Assert.Equal(["Begin AI: eplan_page_create", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
        Assert.Equal(3, _reader.Pages.Count);
    }

    [Fact]
    public void Execute_ANameEplanFindsTaken_IsAlreadyExistsAndRolledBack()
    {
        // In another structure, so neither the plan nor the fingerprint looks at it; dropping "&EFS1" makes it clash.
        _reader.Pages.Add(Page("=GAA+A1/7"));
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/7"));
        args.Fingerprint = Plan(args).Fingerprint;
        _writer.Drop = "&EFS1";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(4, _reader.Pages.Count);
    }

    [Fact]
    public void Execute_ThroughTheRouter_RecordsTheOutcome()
    {
        PageCreateArgs args = Args(Item("=GAA+A1&EFS1/5"));
        args.Fingerprint = Plan(args).Fingerprint;
        var router = new OperationRouter([new PageCreateExecuteOperation(_catalog, _reader, _writer, _transactions)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        router.Invoke(OperationNames.PageCreateExecute, JObject.FromObject(args, BridgeJson.Serializer), "r1");

        Assert.Equal(OutcomeStatus.Committed, router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = "r1" }, "c")["status"]!.ToString());
        Assert.Contains(OperationNames.PageCreateExecute, router.SupportedOperations);
    }

    private static PageSummary Page(string name) => new() { Name = name, Type = "Circuit" };

    private static PageCreateItem Item(string name, string? type = null, string? description = null) => new() { Name = name, PageType = type, Description = description };

    private static PageCreateArgs Args(params PageCreateItem[] pages) => new() { Project = "Line1", Pages = [.. pages] };

    private WritePlan Plan(PageCreateArgs args) =>
        (WritePlan)new PageCreatePlanOperation(_catalog, _reader, _writer).Execute(args)!;

    private PageCreateData Execute(PageCreateArgs args) =>
        (PageCreateData)new PageCreateExecuteOperation(_catalog, _reader, _writer, _transactions).Execute(args)!;
}
