using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class BlockCreationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NamedBlock_ChecksCountAndNameAndKeepsPreviewReadOnly(bool planning)
    {
        var f = new Fixture();
        PlacementTextCreateData result = f.Run(planning);
        Assert.Equal("144_268557", result.Text.Name);
        Assert.Equal(planning ? 0 : 1, f.Writes);
        Assert.Equal(planning ? 0 : 1, f.Transactions.Calls.Count(c => c == "Commit"));
    }

    [Theory]
    [InlineData("stale", ErrorReason.PlanStale)]
    [InlineData("page", ErrorReason.InvalidArgument)]
    [InlineData("child", ErrorReason.InvalidArgument)]
    [InlineData("duplicate", ErrorReason.InvalidArgument)]
    [InlineData("mixed", ErrorReason.InvalidArgument)]
    [InlineData("short", ErrorReason.InvalidArgument)]
    public void InvalidMembersNeverWrite(string problem, string reason)
    {
        var f = new Fixture();
        if (problem == "stale")
        { f.Args.Items![0].Members![0].Digest = new string('b', 64); }
        if (problem == "page")
        { f.Args.Items![0].Members![0].Page = "other"; }
        if (problem == "child")
        { f.Page.Placements[0].Parent = new(); }
        if (problem == "duplicate")
        { f.Args.Items![0].Members![1] = f.Args.Items[0].Members![0]; }
        if (problem == "mixed")
        { f.Args.Items!.Add(new() { Kind = "line" }); }
        if (problem == "short")
        { f.Args.Items![0].Members!.RemoveAt(0); }
        Assert.Equal(reason, Assert.Throws<BridgeErrorException>(() => f.Run(false)).Error.Reason);
        Assert.Empty(f.Transactions.Calls);
        Assert.Equal(0, f.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GroupedMacroLinesCanFormBlock(bool planning)
    {
        var f = new Fixture();
        foreach (DrawingPlacement row in f.Page.Placements)
        { row.Properties["grouped"] = "true"; }
        PlacementTextCreateData result = f.Run(planning);
        Assert.Equal("144_268557", result.Text.Name);
        Assert.Equal(planning ? 0 : 1, f.Writes);
        Assert.Equal(planning ? 0 : 1, f.Transactions.Calls.Count(c => c == "Commit"));
    }

    [Theory]
    [InlineData("count")]
    [InlineData("name")]
    public void WrongReadbackRollsBack(string field)
    {
        var f = new Fixture { Wrong = field };
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => f.Run(false)).Error.Reason);
        Assert.DoesNotContain("Commit", f.Transactions.Calls);
        Assert.Contains("Rollback", f.Transactions.Calls);
    }

    private sealed class Fixture : IPageDrawingReader, IPlacementTextWriter
    {
        public PageDrawingGeometry Page { get; } = new() { Project = "project", Page = "P", Placements = [Row(1), Row(2)] };
        public PlacementTextCreateArgs Args { get; }
        public FakeTransactions Transactions { get; } = new();
        public int Writes { get; private set; }
        public string? Wrong { get; init; }
        public Fixture() => Args = new() { Project = "project", Page = "P", Items = [new() { Kind = "block", Name = "144_268557", Members = Page.Placements.Select(p => new DrawingLocator { Project = "project", Page = "P", Type = p.Locator.Type, Oid = p.Locator.Oid, Digest = p.Locator.Digest }).ToList() }] };
        public PlacementTextCreateData Run(bool planning) => BlockCreation.Run(Args, new FakeCatalog(new OpenProject { Name = "project", Path = "project" }), this, this, Transactions, planning);
        public PageDrawingGeometry Read(string projectPath, string page) => Page;
        public string? Validate(string projectPath, string page, PlacementText text, bool warn = false) => null;
        public PlacementText Create(string projectPath, string page, PlacementText text) { Writes++; return new() { Kind = "block", Name = Wrong == "name" ? "wrong" : text.Name, Count = Wrong == "count" ? 0 : text.Members!.Count }; }
        public List<PlacementText> CreateBatch(string projectPath, string page, IReadOnlyList<PlacementText> items) => items.Select(i => Create(projectPath, page, i)).ToList();
        private static DrawingPlacement Row(long oid) => new() { Kind = "Line", Locator = new() { Project = "project", Page = "P", Type = 4, Oid = oid, Digest = new string('a', 64) } };
    }
}
