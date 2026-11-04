using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageGridTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonPositiveOrNonFiniteGrid_IsInvalidForCreateAndUpdate(double value)
    {
        using var store = new PageFrameStore();
        var create = new PageCreateArgs { Project = "Line1", Pages = [new() { Name = "=GAA+A1&EFS1/2", GridSizeMm = value }] };
        var update = PageFrameStore.Args(new PageUpdateItem { Name = PageFrameStore.First, GridSizeMm = value });
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => new PageCreatePlanOperation(store, store, new GridWriter(new())).Execute(create)).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(store, update)).Error.Reason);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void OverviewGridFour_CreatesAndReadsBackInOneTransaction_AndOmissionUsesDefault()
    {
        var catalog = new FakeCatalog(new OpenProject { Name = "Line1", Path = "P" });
        var reader = new FakePageReader();
        var writer = new GridWriter(reader);
        var transactions = new FakeTransactions();
        var args = new PageCreateArgs { Project = "Line1", Pages = [new() { Name = "=GAA+A1&EFS1/999", PageType = "Overview", GridSizeMm = 4 }, new() { Name = "=GAA+A1&EFS1/998", PageType = "Overview" }] };
        var plan = (WritePlan)new PageCreatePlanOperation(catalog, reader, writer).Execute(args)!;
        Assert.Contains("grid_size_mm=4", plan.Changes[0].NewValue, StringComparison.Ordinal);
        Assert.Equal("Overview", plan.Changes[1].NewValue);
        args.Fingerprint = plan.Fingerprint;
        var result = (PageCreateData)new PageCreateExecuteOperation(catalog, reader, writer, transactions).Execute(args)!;
        Assert.Equal(4, result.Pages[0].GridSizeMm);
        Assert.Null(result.Pages[1].GridSizeMm);
        Assert.Equal(1, writer.ReadGrid("P", "=GAA+A1&EFS1/998"));
        Assert.Single(writer.Writes);
        Assert.Equal(["Begin AI: eplan_page_create", "Commit", "CloseUndo", "Dispose"], transactions.Calls);
    }

    [Fact]
    public void CreateGridReadbackMismatch_RollsBackCreatedPage()
    {
        var catalog = new FakeCatalog(new OpenProject { Name = "Line1", Path = "P" });
        var reader = new FakePageReader();
        var writer = new GridWriter(reader) { IgnoreWrite = true };
        var transactions = new FakeTransactions { Snapshot = () => () => reader.Pages.Clear() };
        var args = new PageCreateArgs { Project = "Line1", Pages = [new() { Name = "=GAA+A1&EFS1/999", GridSizeMm = 4 }] };
        args.Fingerprint = ((WritePlan)new PageCreatePlanOperation(catalog, reader, writer).Execute(args)!).Fingerprint;
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => new PageCreateExecuteOperation(catalog, reader, writer, transactions).Execute(args)).Error.Execution);
        Assert.Empty(reader.Pages);
        Assert.DoesNotContain("Commit", transactions.Calls);
    }

    [Fact]
    public void GridOnlyUpdate_PreviousValueAndNoOp_ArePlannedAndReadBack()
    {
        using var store = new PageFrameStore();
        var args = PageFrameStore.Args(new PageUpdateItem { Name = PageFrameStore.First, GridSizeMm = 4 });
        WritePlan plan = Plan(store, args);
        Assert.Equal(("grid_size_mm", "1", "4"), (plan.Changes[0].Property, plan.Changes[0].OldValue, plan.Changes[0].NewValue));
        PageUpdateData result = Execute(store, args, plan);
        Assert.Equal((4d, 1d), (result.Pages[0].GridSizeMm, result.Pages[0].PreviousGridSizeMm));
        Assert.Empty(store.DescriptionWrites);
        Assert.Empty(store.FrameWrites);
        Assert.Empty(Plan(store, args).Changes);
        Assert.Equal(1, Execute(store, args, Plan(store, args)).Unchanged);
        Assert.Single(store.Calls, c => c.StartsWith("Begin", StringComparison.Ordinal));
    }

    [Fact]
    public void GridChangesAfterPlan_AreStaleBeforeTransaction()
    {
        using var store = new PageFrameStore();
        var args = PageFrameStore.Args(new PageUpdateItem { Name = PageFrameStore.First, GridSizeMm = 4 });
        WritePlan plan = Plan(store, args);
        store.Grids[PageFrameStore.First] = 2;
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(store, args, plan)).Error.Reason);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void GridReadbackMismatch_RollsBackDescriptionAndGrid()
    {
        using var store = new PageFrameStore { GridTamper = _ => 2 };
        var args = PageFrameStore.Args(new PageUpdateItem { Name = PageFrameStore.First, Description = "New", GridSizeMm = 4 });
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Execute(store, args, Plan(store, args))).Error.Execution);
        Assert.Equal(1, store.Grids[PageFrameStore.First]);
        Assert.Equal("Power", store.Read("P", PageFrameStore.First).TextIn("en_US"));
    }

    private static WritePlan Plan(PageFrameStore store, PageUpdateArgs args) => (WritePlan)new PageUpdatePlanOperation(store, store, store, () => store).Execute(args)!;
    private static PageUpdateData Execute(PageFrameStore store, PageUpdateArgs args, WritePlan plan)
    {
        args.Fingerprint = plan.Fingerprint;
        return (PageUpdateData)new PageUpdateExecuteOperation(store, store, store, store, () => store).Execute(args)!;
    }

    private sealed class GridWriter(FakePageReader reader) : IPageWriter, IPageGrid
    {
        public List<string> Writes { get; } = [];
        public bool IgnoreWrite { get; init; }
        private readonly Dictionary<string, double> _grids = [];
        public bool KnowsLanguage(string language) => true;
        public PageSummary Create(string path, PageName name, string type, string? description, string? language)
        {
            var page = new PageSummary { Name = name.Text, Type = type, Description = description };
            reader.Pages.Add(page);
            _grids[name.Text] = 1;
            return page;
        }
        public double ReadGrid(string projectPath, string page) => _grids[page];
        public double WriteGrid(string projectPath, string page, double value)
        {
            Writes.Add(page);
            if (!IgnoreWrite)
            { _grids[page] = value; }
            return ReadGrid(projectPath, page);
        }
    }
}
