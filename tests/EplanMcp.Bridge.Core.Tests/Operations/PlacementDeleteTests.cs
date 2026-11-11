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
    [InlineData("HyperLink")]
    [InlineData("QRCode")]
    [InlineData("Spline")]
    [InlineData("DimensionItem")]
    [InlineData("DimensionCircle")]
    [InlineData("PlaceHolder")]
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
    [InlineData("Terminal")]
    [InlineData("InterruptionPoint")]
    [InlineData("BoxedDevice")]
    [InlineData("PLC")]
    [InlineData("SymbolReference")]
    [InlineData("PotentialDefinition")]
    [InlineData("ConnectionDefinitionPoint")]
    [InlineData("LocationBox")]
    public void FunctionKindsAreAcceptedAndPreviewTheirNameAndKind(string kind)
    {
        var world = new World(kind);
        world.Page.Placements[0].Properties["name"] = "CB6";

        WritePlan plan = world.Plan(world.Input());

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal("delete", change.Action);
        Assert.Equal("function", change.Kind);
        Assert.Equal($"CB6 ({kind})", change.Name);
        Assert.Equal("Removing the last placed function also deletes its device and part references.", change.OldValue);
        Assert.Empty(world.Transactions.Calls);
        Assert.Equal(0, world.Backups);
    }

    [Theory]
    [InlineData("SPECIAL/CO/0")]
    [InlineData("SPECIAL/TLRO/0")]
    [InlineData("SPECIAL/DCP/0")]
    public void SpecialNodeFunctionsAreAccepted(string symbol)
    {
        var world = new World("Function");
        world.Page.Placements[0].Properties["symbol"] = symbol;

        WritePlan plan = world.Plan(world.Input());

        Assert.Equal("function", Assert.Single(plan.Changes).Kind);
    }

    [Fact]
    public void HotAirDeviceSymbolRequestDeletesTheFunctionAndItsPropertyText()
    {
        const string projectPath = @"C:\Users\x\scratch\MX_HotAir\MX_HotAir";
        var world = new World("Function", "MX_HotAir", projectPath, "4");
        DrawingPlacement function = world.Page.Placements[0];
        function.Locator.Type = 17;
        function.Locator.Oid = 2001;
        function.Properties["name"] = "4-CR1";
        function.Properties["symbol"] = "NFPA_symbol/K/1";
        DrawingPlacement property = World.Row("PropertyPlacement", 2001);
        property.Locator = new DrawingLocator
        {
            Project = projectPath,
            Page = "4",
            Type = 17,
            Oid = 2001,
            Digest = new string('b', 64)
        };
        property.Parent = function.Locator;
        world.Page.Placements.Add(property);
        JObject input = JObject.Parse("""
            {
              "project": "MX_HotAir",
              "page": "4",
              "items": [{
                "project": "C:\\Users\\x\\scratch\\MX_HotAir\\MX_HotAir",
                "page": "4",
                "type": 17,
                "oid": 2001,
                "digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
              }],
              "dry_run": false
            }
            """);

        JObject result = world.Execute(input, world.Plan(input));

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(2, Assert.Single(world.Page.Placements).Locator.Oid);
        Assert.Equal("Call eplan_connection_generate for the page so connections follow the removed symbols.", (string?)result["hint"]);
    }

    [Theory]
    [InlineData("DynamicConnectionLine", false)]
    [InlineData("DynamicConnectionLine", true)]
    [InlineData("Connection", false)]
    public void FunctionDeletionAllowsConnectionChangesAndRemovesOnlyOwnedProperties(string connectionKind, bool removeConnection)
    {
        var world = new World("Function")
        {
            AfterRemove = page =>
            {
                DrawingPlacement connection = page.Placements.Single(p => p.Locator.Oid == 3);
                if (removeConnection)
                {
                    page.Placements.Remove(connection);
                }
                else
                {
                    connection.Locator.Digest = new string('c', 64);
                }
            }
        };
        DrawingPlacement property = World.Row("PropertyPlacement", 1);
        property.Locator.Digest = new string('b', 64);
        property.Parent = world.Page.Placements[0].Locator;
        DrawingPlacement otherProperty = World.Row("PropertyPlacement", 2);
        otherProperty.Locator.Digest = new string('d', 64);
        otherProperty.Parent = world.Page.Placements[1].Locator;
        world.Page.Placements.AddRange([property, otherProperty, World.Row(connectionKind, 3)]);
        JObject input = world.Input();

        JObject result = world.Execute(input, world.Plan(input));

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.DoesNotContain(world.Page.Placements, p => p.Locator.Oid == 1);
        Assert.Equal(new string('a', 64), world.Page.Placements.Single(p => p.Kind == "Line").Locator.Digest);
        Assert.Equal(new string('d', 64), world.Page.Placements.Single(p => p.Kind == "PropertyPlacement").Locator.Digest);
        Assert.Equal(["Begin AI: eplan_placement_delete", "Commit", "CloseUndo", "Dispose"], world.Transactions.Calls);
    }

    [Fact]
    public void MixedFunctionAndGraphicDeletionAllowsNewAutoconnectDrawing()
    {
        var world = new World("Function")
        {
            AfterRemove = page => page.Placements.Add(World.Row("DynamicConnectionLine", 3))
        };
        world.Page.Placements.Add(World.Row("Text", 4));
        JObject input = world.Input();
        ((JArray)input["items"]!).Add(JObject.FromObject(world.Page.Placements[1].Locator, Bridge.Core.Protocol.BridgeJson.Serializer));

        JObject result = world.Execute(input, world.Plan(input));

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal([3L, 4L], world.Page.Placements.Select(p => p.Locator.Oid).Order());
        Assert.Equal(new string('a', 64), world.Page.Placements.Single(p => p.Kind == "Text").Locator.Digest);
    }

    [Theory]
    [InlineData("change")]
    [InlineData("remove")]
    [InlineData("add")]
    [InlineData("target")]
    [InlineData("target_connection")]
    [InlineData("property")]
    public void FunctionDeletionRollsBackUnrelatedChangesOrRemainingTargets(string mutation)
    {
        var world = new World("Function")
        {
            AfterRemove = page =>
            {
                switch (mutation)
                {
                    case "change":
                        page.Placements[0].Locator.Digest = new string('c', 64);
                        break;
                    case "remove":
                        page.Placements.Clear();
                        break;
                    case "add":
                        page.Placements.Add(World.Row("Text", 4));
                        break;
                    case "target":
                        DrawingPlacement target = World.Row("Function", 1);
                        target.Locator.Digest = new string('c', 64);
                        page.Placements.Add(target);
                        break;
                    case "target_connection":
                        DrawingPlacement reclassifiedTarget = World.Row("DynamicConnectionLine", 1);
                        reclassifiedTarget.Locator.Digest = new string('c', 64);
                        page.Placements.Add(reclassifiedTarget);
                        break;
                    case "property":
                        DrawingPlacement property = World.Row("PropertyPlacement", 1);
                        property.Parent = World.Row("Function", 1).Locator;
                        property.Locator.Digest = new string('c', 64);
                        page.Placements.Add(property);
                        break;
                }
            }
        };
        JObject input = world.Input();
        WritePlan plan = world.Plan(input);
        string before = world.Page.Fingerprint();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Execute(input, plan)).Error;

        Assert.Equal(ErrorReason.EplanOperationFailed, error.Reason);
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(before, world.Page.Fingerprint());
    }

    [Fact]
    public void GraphicsOnlyDeletionStillRollsBackAutoconnectChanges()
    {
        var world = new World("Line")
        {
            AfterRemove = page => page.Placements.Single(p => p.Locator.Oid == 3).Locator.Digest = new string('c', 64)
        };
        world.Page.Placements.Add(World.Row("DynamicConnectionLine", 3));
        JObject input = world.Input();
        WritePlan plan = world.Plan(input);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Execute(input, plan)).Error;

        Assert.Equal(ErrorReason.EplanOperationFailed, error.Reason);
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(plan.Fingerprint, world.Page.Fingerprint());
    }

    [Theory]
    [InlineData("Connection")]
    [InlineData("DynamicConnectionLine")]
    [InlineData("PropertyPlacement")]
    [InlineData("ContextPropertyPlacement")]
    [InlineData("Block")]
    public void UnsupportedObjectsAreRefusedWithReasonBeforeBackup(string kind)
    {
        var world = new World(kind);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Plan(world.Input())).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains(kind, error.Message, StringComparison.Ordinal);
        Assert.Empty(world.Transactions.Calls);
        Assert.Equal(0, world.Backups);
    }

    [Theory]
    [InlineData("Function")]
    [InlineData("PropertyPlacement")]
    public void FunctionBlockMembersAndPropertyChildrenKeepTheirRejectionMessage(string kind)
    {
        var world = new World(kind);
        world.Page.Placements[0].Parent = new DrawingLocator { Type = 4, Oid = 99 };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Plan(world.Input())).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal("Invalid arguments: Block members and property text cannot be deleted separately.", error.Message);
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
        public Action<PageDrawingGeometry>? AfterRemove { get; init; }
        private string ProjectName { get; }
        public World(string kind, string projectName = "project", string projectPath = "project", string pageName = "page")
        {
            ProjectName = projectName;
            Page = new() { Project = projectPath, Page = pageName, GridSize = 4, Placements = [Row(kind, 1), Row("Line", 2)] };
            foreach (DrawingPlacement placement in Page.Placements)
            {
                placement.Locator.Project = projectPath;
                placement.Locator.Page = pageName;
            }
            Transactions.Snapshot = () =>
            {
                PageDrawingGeometry old = Read(projectPath, pageName)!;
                return () => Page = old;
            };
        }
        public static DrawingPlacement Row(string kind, long id) => new() { Kind = kind, Locator = new() { Project = "project", Page = "page", Type = 4, Oid = id, Digest = new string('a', 64) } };
        public PageDrawingGeometry? Read(string projectPath, string pageName) => MissingPage ? null : JObject.FromObject(Page).ToObject<PageDrawingGeometry>()!;
        public Action Prepare(string project, string page, IReadOnlyList<DrawingLocator> items) => () =>
        {
            Page.Placements.RemoveAll(p => items.Any(l => DrawingLocators.Matches(l, p.Locator) || p.Parent is { } parent && parent.Type == l.Type && parent.Oid == l.Oid));
            AfterRemove?.Invoke(Page);
            if (Unexpected)
            { Page.Placements[0].Locator.Digest = new string('c', 64); }
            if (ThrowAfterRemove)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Injected failure."); }
        };
        public JObject Input(bool all = false) => new() { ["project"] = ProjectName, ["page"] = Page.Page, ["items"] = JArray.FromObject(Page.Placements.Take(all ? 100 : 1).Select(p => p.Locator), Bridge.Core.Protocol.BridgeJson.Serializer) };
        private PlacementDeleteOperation Operation(bool execute) => new(new FakeCatalog(new OpenProject { Name = ProjectName, Path = Page.Project }), this, this, Transactions, execute, (_, _) => Backups++);
        public WritePlan Plan(JObject input) => ((JObject)Operation(false).Execute(input)!).ToObject<WritePlan>()!;
        public JObject Execute(JObject input, WritePlan plan) => (JObject)Operation(true).Execute(new JObject { ["request"] = input.DeepClone(), ["fingerprint"] = plan.Fingerprint })!;
    }
}
