using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutEnclosurePlaceTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private readonly FakeLayout _layout = new();
    private readonly FakePartsDatabase _parts = new();
    private readonly FakeTransactions _transactions = new();

    public LayoutEnclosurePlaceTests()
    {
        _parts.Content.Parts.Add(new PartSearchItem { PartNumber = "RIT.1055500" });
        _layout.Record.Spaces.Add(new LayoutSpace { Designation = "UC2", Enclosures = [new LayoutEnclosure { Id = 5, Parts = ["RIT.1034500"] }] });
    }

    [Fact]
    public void NewSpace_PlansSpaceAndEnclosure_ThenPlacesInOneTransactionAndReadsBack()
    {
        var args = new LayoutEnclosurePlaceArgs { Project = "Line1", Space = "+UC1", Part = "rit.1055500" };
        WritePlan plan = Plan(args);
        Assert.Equal(["installation space", "enclosure"], plan.Changes.Select(c => c.Kind));
        Assert.Equal("RIT.1055500 variant 1", plan.Changes[1].NewValue);

        args.Fingerprint = plan.Fingerprint;
        LayoutEnclosurePlaceData data = Execute(args);

        Assert.Equal(("UC1", "RIT.1055500", "1"), _layout.Placed!.Value);
        Assert.Equal(("UC1", true, 42L), (data.Space, data.SpaceCreated, data.Enclosure.Id));
        Assert.Equal(["Begin AI: eplan_enclosure_place", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void SpaceWithAnEnclosure_IsAlreadyExists()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(new LayoutEnclosurePlaceArgs { Project = "Line1", Space = "UC2", Part = "RIT.1055500" }));
        Assert.Equal(ErrorReason.AlreadyExists, error.Error.Reason);
    }

    [Theory]
    [InlineData("UC 1", "RIT.1055500", ErrorReason.InvalidArgument)]
    [InlineData("UC1", "", ErrorReason.InvalidArgument)]
    [InlineData("UC1", "RIT.9999999", ErrorReason.PartNotFound)]
    public void BadSpaceOrPart_IsRefusedBeforeAnyWrite(string space, string part, string reason)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(new LayoutEnclosurePlaceArgs { Project = "Line1", Space = space, Part = part }));
        Assert.Equal(reason, error.Error.Reason);
        Assert.Null(_layout.Placed);
    }

    [Fact]
    public void ReadBackWithoutAMountingPanel_RollsBack()
    {
        _layout.PanelAfterPlacing = false;
        var args = new LayoutEnclosurePlaceArgs { Project = "Line1", Space = "UC1", Part = "RIT.1055500" };
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void LayoutChangedAfterThePlan_IsPlanStale()
    {
        var args = new LayoutEnclosurePlaceArgs { Project = "Line1", Space = "UC1", Part = "RIT.1055500" };
        args.Fingerprint = Plan(args).Fingerprint;
        _layout.Record.Spaces.Add(new LayoutSpace { Designation = "UC3" });

        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_transactions.Calls);
    }

    private WritePlan Plan(LayoutEnclosurePlaceArgs args) => (WritePlan)new LayoutEnclosurePlacePlanOperation(new FakeCatalog(_line1), _layout, _parts).Execute(args)!;

    private LayoutEnclosurePlaceData Execute(LayoutEnclosurePlaceArgs args) =>
        (LayoutEnclosurePlaceData)new LayoutEnclosurePlaceExecuteOperation(new FakeCatalog(_line1), _layout, _parts, _layout, _transactions).Execute(args)!;

    private sealed class FakeLayout : ILayoutReader, ILayoutWriter
    {
        public LayoutRecord Record { get; } = new();
        public (string Space, string Part, string Variant)? Placed { get; private set; }
        public bool PanelAfterPlacing { get; set; } = true;

        public LayoutRecord Read(string projectPath) => Record;

        public long PlaceEnclosure(string projectPath, string space, string part, string variant)
        {
            Placed = (space, part, variant);
            LayoutSpace target = Record.Spaces.FirstOrDefault(s => s.Designation == space) ?? new LayoutSpace { Designation = space };
            if (!Record.Spaces.Contains(target))
            { Record.Spaces.Add(target); }
            target.Enclosures.Add(new LayoutEnclosure { Id = 42, Parts = [part], Panel = PanelAfterPlacing ? new LayoutSurface { Width = 749, Height = 570 } : null });
            return 42;
        }
    }
}
