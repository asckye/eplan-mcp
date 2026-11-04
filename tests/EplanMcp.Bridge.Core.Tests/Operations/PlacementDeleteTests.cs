using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementDeleteTests
{
    [Theory]
    [InlineData("Line")]
    [InlineData("Arc")]
    [InlineData("PolyLine")]
    [InlineData("Rectangle")]
    [InlineData("Text")]
    [InlineData("PathText")]
    [InlineData("Image")]
    [InlineData("MacroBox")]
    public void PreviewDoesNotWrite_ExecutionDeletesOnlySelectedKindInOneTransaction(string kind)
    {
        var world = new World(kind);
        JObject input = world.Input();
        WritePlan plan = world.Plan(input);
        Assert.Single(plan.Changes);
        Assert.Empty(world.Transactions.Calls);
        Assert.Equal(0, world.Backups);
        JObject result = world.Execute(input, plan);
        Assert.Equal("committed", (string?)result["execution"]);
        Assert.Single(world.Page.Placements);
        Assert.Equal(2, world.Page.Placements[0].Locator.Oid);
        Assert.Equal(1, world.Backups);
        Assert.Equal(["Begin AI: eplan_placement_delete", "Commit", "CloseUndo", "Dispose"], world.Transactions.Calls);
    }

    [Fact]
    public void MacroBoxDeletionIncludesOwnedPropertyTextAndKeepsReferencedGraphics()
    {
        var world = new World("MacroBox");
        DrawingPlacement child = World.Row("PropertyPlacement", 3);
        child.Parent = world.Page.Placements[0].Locator;
        world.Page.Placements.Add(child);
        JObject input = world.Input();
        world.Execute(input, world.Plan(input));
        Assert.Equal(2, Assert.Single(world.Page.Placements).Locator.Oid);
    }

    [Theory]
    [InlineData("Function")]
    [InlineData("Connection")]
    [InlineData("ContextPropertyPlacement")]
    [InlineData("Block")]
    [InlineData("PLC")]
    public void UnsupportedObjectsAreRefusedWithReasonBeforeBackup(string kind)
    {
        var world = new World(kind);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Plan(world.Input())).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains(kind, error.Message, StringComparison.Ordinal);
        Assert.Empty(world.Transactions.Calls);
        Assert.Equal(0, world.Backups);
    }

    [Fact]
    public void BlockMembersAndPropertyChildrenAreRefused()
    {
        var world = new World("Line");
        { world.Page.Placements[0].Parent = new() { Type = 4, Oid = 99 }; }
        Assert.Contains("cannot be deleted", Assert.Throws<BridgeErrorException>(() => world.Plan(world.Input())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupedMacroLineCanBeDeletedInOneTransaction()
    {
        var world = new World("Line");
        world.Page.Placements[0].Properties["grouped"] = "true";
        JObject input = world.Input();
        world.Execute(input, world.Plan(input));
        Assert.Equal(2, Assert.Single(world.Page.Placements).Locator.Oid);
        Assert.Equal(["Begin AI: eplan_placement_delete", "Commit", "CloseUndo", "Dispose"], world.Transactions.Calls);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("page")]
    [InlineData("project")]
    [InlineData("oid")]
    public void StaleLocatorDoesNotDeleteAnything(string field)
    {
        var world = new World("Line");
        JObject input = world.Input();
        input["items"]![0]![field] = field == "oid" ? new JValue(99) : new JValue(field == "digest" ? new string('b', 64) : "other");
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => world.Plan(input)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void InvalidBatchSizeIsRefused(int count)
    {
        var world = new World("Line");
        JObject input = world.Input();
        input["items"] = new JArray(Enumerable.Range(0, count).Select(_ => input["items"]![0]!.DeepClone()));
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => world.Plan(input)).Error.Reason);
    }

    [Theory]
    [InlineData("type", "2147483648")]
    [InlineData("oid", "9223372036854775808")]
    public void OutOfRangeLocatorNumbersAreArgumentErrors(string field, string number)
    {
        var world = new World("Line");
        JObject input = world.Input();
        input["items"]![0]![field] = JToken.Parse(number);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => world.Plan(input)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    [Fact]
    public void MissingBoundsDoNotBlockDeletionAndOneHundredItemsAreSupported()
    {
        var world = new World("Line");
        world.Page.PageSize = null;
        world.Page.Placements = [.. Enumerable.Range(1, 100).Select(i => World.Row("Line", i))];
        JObject input = world.Input(all: true);
        world.Execute(input, world.Plan(input));
        Assert.Empty(world.Page.Placements);
    }

    [Fact]
    public void AChangedPageBeforeExecutionIsStale()
    {
        var world = new World("Line");
        JObject input = world.Input();
        WritePlan plan = world.Plan(input);
        world.Page.Placements[1].Locator.Digest = new string('b', 64);
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => world.Execute(input, plan)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    [Fact]
    public void EmptyProjectDoesNotSelectCurrentProject()
    {
        var world = new World("Line");
        JObject input = world.Input();
        input["project"] = " ";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => world.Plan(input)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    [Fact]
    public void DisappearingPageMakesExecutionStale()
    {
        var world = new World("Line");
        JObject input = world.Input();
        WritePlan plan = world.Plan(input);
        world.MissingPage = true;
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => world.Execute(input, plan)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialFailureOrUnexpectedChangeRollsBackEverything(bool unexpected)
    {
        var world = new World("Line") { Unexpected = unexpected, ThrowAfterRemove = !unexpected };
        JObject input = world.Input();
        WritePlan plan = world.Plan(input);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Execute(input, plan)).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(2, world.Page.Placements.Count);
    }

    private sealed class World : IPageDrawingReader, IPlacementRemover
    {
        public PageDrawingGeometry Page { get; set; }
        public FakeTransactions Transactions { get; } = new();
        public int Backups { get; private set; }
        public bool Unexpected { get; init; }
        public bool ThrowAfterRemove { get; init; }
        public bool MissingPage { get; set; }
        public World(string kind)
        {
            Page = new() { Project = "project", Page = "page", GridSize = 4, Placements = [Row(kind, 1), Row("Line", 2)] };
            Transactions.Snapshot = () => { PageDrawingGeometry old = Read("project", "page")!; return () => Page = old; };
        }
        public static DrawingPlacement Row(string kind, long id) => new() { Kind = kind, Locator = new() { Project = "project", Page = "page", Type = 4, Oid = id, Digest = new string('a', 64) } };
        public PageDrawingGeometry? Read(string projectPath, string pageName) => MissingPage ? null : JObject.FromObject(Page).ToObject<PageDrawingGeometry>()!;
        public Action Prepare(string project, string page, IReadOnlyList<DrawingLocator> items) => () =>
        {
            Page.Placements.RemoveAll(p => items.Any(l => DrawingLocators.Matches(l, p.Locator) || p.Parent is { } parent && parent.Type == l.Type && parent.Oid == l.Oid));
            if (Unexpected)
            { Page.Placements[0].Locator.Digest = new string('c', 64); }
            if (ThrowAfterRemove)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Injected failure."); }
        };
        public JObject Input(bool all = false) => new() { ["project"] = "project", ["page"] = "page", ["items"] = JArray.FromObject(Page.Placements.Take(all ? 100 : 1).Select(p => p.Locator), Bridge.Core.Protocol.BridgeJson.Serializer) };
        private PlacementDeleteOperation Operation(bool execute) => new(new FakeCatalog(new OpenProject { Name = "project", Path = "project" }), this, this, Transactions, execute, (_, _) => Backups++);
        public WritePlan Plan(JObject input) => ((JObject)Operation(false).Execute(input)!).ToObject<WritePlan>()!;
        public JObject Execute(JObject input, WritePlan plan) => (JObject)Operation(true).Execute(new JObject { ["request"] = input.DeepClone(), ["fingerprint"] = plan.Fingerprint })!;
    }
}
