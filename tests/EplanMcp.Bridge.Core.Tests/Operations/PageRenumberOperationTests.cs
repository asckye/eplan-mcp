using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// page.renumber.plan and page.renumber.execute against a fake that renumbers per structure the way spike S12 found
/// EPLAN does (api-notes A18: /90, /91, /92 from 200 in steps of 10 became /200, /210, /220): exact names, the change
/// list, the fingerprint, the backup before the transaction and the check for names taken twice. Medium: the backup goes
/// into a real temporary folder.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PageRenumberOperationTests : IDisposable
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };
    private static readonly string[] _probe = ["=GAA+A1&EFS1/90", "=GAA+A1&EFS1/91", "=GAA+A1&EFS1/92"];
    private static readonly string[] _others = ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2", "=GAA+A2&EFS1/1"];

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-pages-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakePageRenumberer _renumberer;
    private readonly FakeProjectBackup _backup;
    private readonly FakeTransactions _transactions = new();

    public PageRenumberOperationTests()
    {
        _pages.Pages.AddRange([.. _others.Concat(_probe).Select(name => new PageSummary { Name = name, Type = "Circuit" })]);
        _renumberer = new FakePageRenumberer(_pages);
        _backup = new FakeProjectBackup(_pages);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Plan_HasOneChangePerPage_AndTheNumberingInTheDetails()
    {
        WritePlan plan = Plan(Args(200, 10));

        Assert.Equal(_probe, plan.Changes.Select(c => c.Name));
        Assert.All(plan.Changes, c => Assert.Equal(("update", "page", "page number", null), (c.Action, c.Kind, c.Property, c.NewValue)));
        Assert.Equal("from 200, step 10, per structure, subpages: retain, keep text: no", plan.Details!["numbering"]);
        Assert.Equal(("200", "10", "false", "retain"), (plan.Details["start"], plan.Details["step"], plan.Details["keepText"], plan.Details["subpages"]));
        Assert.Empty(_renumberer.Renumbers);
    }

    [Fact]
    public void Plan_Fingerprint_DependsOnThePagesStructuresOnly()
    {
        string first = Plan(Args(200, 10)).Fingerprint;
        _pages.Pages.Add(new PageSummary { Name = "=GAA+A2&EFS1/2" });
        string elsewhere = Plan(Args(200, 10)).Fingerprint;
        _pages.Pages.Add(new PageSummary { Name = "=GAA+A1&EFS1/200" });

        Assert.Equal(first, elsewhere);
        Assert.NotEqual(first, Plan(Args(200, 10)).Fingerprint);
    }

    [Fact]
    public void Plan_NamesEveryMissingPage_AndEveryArgumentProblemAtOnce()
    {
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Plan(new PageRenumberArgs { Project = "Line1", Pages = ["=gaa+a1&efs1/90", "=X/1"], Start = 1 })).Error;
        BridgeError invalid = Assert.Throws<BridgeErrorException>(() => Plan(new PageRenumberArgs { Pages = [_probe[0], " ", "=gaa+A1&EFS1/90"], Start = 0, Step = 0, Subpages = "keep" })).Error;

        Assert.Equal((ErrorReason.PageNotFound, "=gaa+a1&efs1/90\n=X/1"), (missing.Reason, missing.Metadata!["missing"]));
        Assert.Equal(
            "'project' is required for writes: a name or path from eplan_project_list.\npages[1] is empty; give a full page name such as =GAA+A1&EFS1/90 (eplan_page_list).\npages[2] repeats pages[0] (letter case does not count).\n'start' must be at least 1.\n'step' must be at least 1.\n'subpages' must be one of retain, consecutive, to_main.",
            invalid.Metadata!["violations"]);
    }

    [Fact]
    public void Execute_BacksUpFirst_ThenRenumbersInOneTransaction_AndReadsTheNamesBack()
    {
        PageRenumberArgs args = Args(200, 10);
        args.Fingerprint = Plan(args).Fingerprint;
        _transactions.Snapshot = () =>
        {
            Assert.Single(_backup.Copies);
            return () => { };
        };

        PageRenumberData data = Execute(args);

        Assert.Equal([("=GAA+A1&EFS1/90", "=GAA+A1&EFS1/200"), ("=GAA+A1&EFS1/91", "=GAA+A1&EFS1/210"), ("=GAA+A1&EFS1/92", "=GAA+A1&EFS1/220")], data.Changed.Select(c => (c.From, c.To)));
        Assert.Equal(0, data.Unchanged);
        Assert.Equal(["Begin AI: eplan_page_renumber", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal((200, 10, false, "retain"), (_renumberer.Renumbers[0].Start, _renumberer.Renumbers[0].Step, _renumberer.Renumbers[0].KeepText, _renumberer.Renumbers[0].Subpages));
    }

    [Fact]
    public void Execute_ANumberTakenTwice_RollsEverythingBack()
    {
        PageRenumberArgs args = Args(1, 1);
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.RolledBack, "=GAA+A1&EFS1/1\n=GAA+A1&EFS1/2"), (error.Reason, error.Execution, error.Metadata!["existing"]));
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_ANameEplanRefuses_IsAlreadyExistsAndRolledBack()
    {
        PageRenumberArgs args = Args(200, 10);
        args.Fingerprint = Plan(args).Fingerprint;
        _renumberer.Refuse = true;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.RolledBack), (error.Reason, error.Execution));
    }

    [Fact]
    public void Execute_WhenTheStructureChanged_IsStaleAndNothingIsBackedUp()
    {
        PageRenumberArgs args = Args(200, 10);
        args.Fingerprint = Plan(args).Fingerprint;
        _pages.Pages.Add(new PageSummary { Name = "=GAA+A1&EFS1/3" });

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_backup.Copies);
    }

    [Fact]
    public void Execute_WhenTheBackupFails_NothingIsRenumbered()
    {
        PageRenumberArgs args = Args(200, 10);
        args.Fingerprint = Plan(args).Fingerprint;
        _backup.Failure = new IOException("disk full");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_renumberer.Renumbers);
    }

    private static PageRenumberArgs Args(int start, int step) => new() { Project = "Line1", Pages = [.. _probe], Start = start, Step = step };

    private WritePlan Plan(PageRenumberArgs args) => (WritePlan)new PageRenumberPlanOperation(new FakeCatalog(_project), _pages, _renumberer).Execute(args)!;

    private PageRenumberData Execute(PageRenumberArgs args)
    {
        args.Backup = new BackupTarget { Path = Path.Combine(_folder, "Line1.elk"), MaxSeconds = 33 };
        return (PageRenumberData)new PageRenumberExecuteOperation(new FakeCatalog(_project), _pages, _renumberer, _backup, _transactions).Execute(args)!;
    }
}
