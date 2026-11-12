using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutPlaceTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private readonly FakeLayout _layout = new();
    private readonly FakePartsDatabase _parts = new();
    private readonly FakeTransactions _transactions = new();

    public LayoutPlaceTests()
    {
        foreach (string part in new[] { "PXC.0806602", "PXC.3240192", "SIE.3LD2504-0TK53", "SE.XB4BS844415" })
        { _parts.Content.Parts.Add(new PartSearchItem { PartNumber = part }); }
        _layout.Record.Spaces.Add(new LayoutSpace
        {
            Designation = "UC1",
            Enclosures = [new LayoutEnclosure { Id = 1, Panel = new LayoutSurface { Width = 749, Height = 570 }, Door = new LayoutSurface { Width = 795, Height = 595 } }],
        });
        _layout.Record.Devices.AddRange([new LayoutDevice { Tag = "=GAA+UC1-1QA1", Part = "SIE.3LD2504-0TK53" }, new LayoutDevice { Tag = "=KF+UC1-12SJ1", Part = "SE.XB4BS844415" }]);
    }

    [Fact]
    public void RailThenDevicesOnItAndTheDoor_ArePlacedInOrderInOneTransaction_DevicesUnderTheirTags()
    {
        var args = new LayoutPlaceArgs
        {
            Project = "Line1",
            Space = "UC1",
            Items =
            [
                new LayoutPlaceItem { Kind = "rail", Ref = "R1", Part = "PXC.0806602", LengthMm = 700, X = 20, Y = 440 },
                new LayoutPlaceItem { Kind = "device", Tag = "=GAA+UC1-1QA1", On = "rail", Rail = "R1", OffsetMm = 10 },
                new LayoutPlaceItem { Kind = "device", Tag = "=KF+UC1-12SJ1", On = "door", X = 300, Y = 400 },
            ],
        };
        WritePlan plan = Plan(args);
        Assert.Equal(["rail", "device", "device"], plan.Changes.Select(c => c.Kind));
        Assert.Equal("SIE.3LD2504-0TK53, on rail at 10 mm", plan.Changes[1].NewValue);

        args.Fingerprint = plan.Fingerprint;
        LayoutPlaceData data = Execute(args);

        Assert.Equal([(null, (long?)null), ("=GAA+UC1-1QA1", 100L), ("=KF+UC1-12SJ1", null)], _layout.Placed.Select(p => (p.Step.Tag, p.Rail)));
        Assert.Equal([100L, 101L, 102L], data.Items.Select(i => i.Id));
        Assert.Empty(data.Overlaps);
        Assert.Equal(["Begin AI: eplan_layout_place", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData("rail", null, 700.0, 20.0, 440.0, "'part' is required")]
    [InlineData("rail", "PXC.0806602", 5.0, 20.0, 440.0, "'length_mm' must be 10 to 3000")]
    [InlineData("rail", "PXC.0806602", 740.0, 20.0, 440.0, "runs off the panel")]
    [InlineData("duct", "PXC.3240192", 100.0, 20.0, 900.0, "must lie on the panel")]
    [InlineData("rail", "PXC.9999999", 100.0, 20.0, 440.0, "no part PXC.9999999")]
    [InlineData("socket", "PXC.0806602", 100.0, 20.0, 440.0, "'kind' must be")]
    public void BadRailOrDuct_IsRefusedBeforeAnyWrite(string kind, string? part, double length, double x, double y, string violation)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Single(new LayoutPlaceItem { Kind = kind, Part = part, LengthMm = length, X = x, Y = y }))).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains(violation, error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_layout.Placed);
    }

    [Fact]
    public void UnknownOrAlreadyPlacedDevice_AndAMissingRail_AreRefused()
    {
        _layout.Record.Names3D.Add("=KF+UC1-12SJ1");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new LayoutPlaceArgs
        {
            Project = "Line1",
            Space = "UC1",
            Items =
            [
                new LayoutPlaceItem { Kind = "device", Tag = "=X-9", On = "panel", X = 1, Y = 1 },
                new LayoutPlaceItem { Kind = "device", Tag = "=KF+UC1-12SJ1", On = "door", X = 1, Y = 1 },
                new LayoutPlaceItem { Kind = "device", Tag = "=GAA+UC1-1QA1", Rail = "R9", OffsetMm = 0 },
            ],
        })).Error;
        string violations = error.Metadata!["violations"];
        Assert.Contains("no schematic device with a part has the tag =X-9", violations, StringComparison.Ordinal);
        Assert.Contains("=KF+UC1-12SJ1 already has a 3D placement", violations, StringComparison.Ordinal);
        Assert.Contains("'rail' must name an earlier rail", violations, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceOnTheTop_IsPlacedWhenTheEnclosureHasATop_AndRefusedOtherwise()
    {
        var args = new LayoutPlaceArgs { Project = "Line1", Space = "UC1", Items = [new LayoutPlaceItem { Kind = "device", Tag = "=GAA+UC1-1QA1", On = "top", X = 80, Y = 120 }] };
        Assert.Contains("the enclosure has no top surface", Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"], StringComparison.Ordinal);

        _layout.Record.Spaces[0].Enclosures[0].Top = new LayoutSurface { Id = 3, Width = 800, Height = 280 };
        args.Fingerprint = Plan(args).Fingerprint;
        Execute(args);
        Assert.Equal(("top", 80.0, 120.0), (_layout.Placed.Single().Step.Surface, _layout.Placed.Single().Step.X, _layout.Placed.Single().Step.Y));
        args.Items[0].Y = 300;
        Assert.Contains("must lie on the top (0-800 x 0-280 mm)", Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void SpaceWithoutEnclosure_IsRefused()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new LayoutPlaceArgs { Project = "Line1", Space = "UC9", Items = [new LayoutPlaceItem { Kind = "rail" }] })).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal("UC9", error.Metadata!["space"]);
    }

    [Fact]
    public void Overlaps_SkipParentAndChild_AndNameTheOtherObject()
    {
        LayoutObject[] objects =
        [
            new() { Id = 100, Kind = "MountingRail", Box = [0, 0, 0, 700, 15, 35] },
            new() { Id = 101, Kind = "Component", Tag = "=A-1", ParentId = 100, Box = [10, -20, -30, 90, 110, 95] },
            new() { Id = 7, Kind = "Duct", Parts = ["PXC.3240192"], Box = [50, -10, 40, 700, 60, 40] },
        ];
        LayoutPlaced[] placed = [new() { Index = 1, Id = 101, Tag = "=A-1", Box = [10, -20, -30, 90, 110, 95] }];

        List<string> overlaps = LayoutPlaceRequest.Overlaps(placed, objects);

        Assert.Equal(["items[1] (=A-1) overlaps Duct 7 (PXC.3240192)"], overlaps);
    }

    private static LayoutPlaceArgs Single(LayoutPlaceItem item) => new() { Project = "Line1", Space = "UC1", Items = [item] };

    private WritePlan Plan(LayoutPlaceArgs args) => (WritePlan)new LayoutPlacePlanOperation(new FakeCatalog(_line1), _layout, _parts).Execute(args)!;

    private LayoutPlaceData Execute(LayoutPlaceArgs args) =>
        (LayoutPlaceData)new LayoutPlaceExecuteOperation(new FakeCatalog(_line1), _layout, _parts, _layout, _transactions).Execute(args)!;

    private sealed class FakeLayout : ILayoutReader, ILayoutPlacer
    {
        public LayoutRecord Record { get; } = new();
        public List<(LayoutPlaceStep Step, long? Rail)> Placed { get; } = [];

        public LayoutRecord Read(string projectPath) => Record;

        public LayoutPlaced Place(string projectPath, long enclosureId, LayoutPlaceStep item, long? railId)
        {
            Placed.Add((item, railId));
            long id = 100 + Placed.Count - 1;
            double[] box = [item.Index * 1000, 0, 0, 10, 10, 10];
            Record.Objects.Add(new LayoutObject { Space = "UC1", Id = id, Kind = item.Kind == "rail" ? "MountingRail" : "Component", Tag = item.Tag, ParentId = railId, Box = box });
            return new LayoutPlaced { Index = item.Index, Kind = item.Kind, Id = id, Tag = item.Tag, Part = item.Part, Box = box };
        }
    }
}
