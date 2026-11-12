using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGetOperationTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private readonly FakeLayoutReader _layout = new();

    public LayoutGetOperationTests()
    {
        _layout.Record.Spaces.AddRange(
        [
            new LayoutSpace { Designation = "UC1", Name = "+UC1", Enclosures = [new LayoutEnclosure { Id = 1, Parts = ["RIT.1055500"] }] },
            new LayoutSpace { Designation = "UC2", Name = "+UC2" },
        ]);
        _layout.Record.Objects.AddRange(
        [
            new LayoutObject { Space = "UC1", Id = 3, Kind = "MountingRail" },
            new LayoutObject { Space = "UC2", Id = 9, Kind = "Duct" },
            new LayoutObject { Space = "UC1", Id = 4, Kind = "Component", Tag = "=MA1+UC1-8TA1" },
            new LayoutObject { Space = "UC1", Id = 1, Kind = "Cabinet" },
        ]);
        _layout.Record.Devices.AddRange(
        [
            new LayoutDevice { Tag = "=MA1+UC1-8TA1", Part = "SIE.6SL3210-1KE18-8UF1" },
            new LayoutDevice { Tag = "=K1+UC1-KE1", Part = "SIE.6ES7214-1AH50-0XB0" },
            new LayoutDevice { Tag = "=K1+UC1-KE1", Part = "SIE.6ES7214-1AH50-0XB0" },
        ]);
        _layout.Record.Names3D.Add("=MA1+UC1-8TA1");
    }

    [Fact]
    public void OneSpace_EnclosuresFirst_ThenEplanOrder_AndOnlyDevicesWithoutA3DFunction()
    {
        LayoutGetData data = Run(new LayoutGetArgs { Space = "+UC1", Limit = 10 });

        Assert.Equal(["UC1"], data.Spaces.Select(s => s.Designation));
        Assert.Equal([1L, 3L, 4L], data.Objects.Select(o => o.Id));
        Assert.Equal(3, data.Total);
        Assert.Equal(["=K1+UC1-KE1"], data.Unplaced.Select(d => d.Tag));
        Assert.Equal(1, data.UnplacedTotal);
        Assert.Equal("Line1", data.Project);
    }

    [Fact]
    public void AllSpaces_AreSlicedByOffsetAndLimit()
    {
        LayoutGetData data = Run(new LayoutGetArgs { Offset = 1, Limit = 2 });

        Assert.Equal(2, data.Spaces.Count);
        Assert.Equal([3L, 9L], data.Objects.Select(o => o.Id));
        Assert.Equal(4, data.Total);
    }

    [Fact]
    public void UnknownSpace_IsAnInvalidArgumentNamingIt()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Run(new LayoutGetArgs { Space = "UC9", Limit = 10 }));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal("UC9", error.Error.Metadata!["space"]);
    }

    private LayoutGetData Run(LayoutGetArgs args) => (LayoutGetData)new LayoutGetOperation(new FakeCatalog(_line1), _layout).Execute(args)!;

    private sealed class FakeLayoutReader : ILayoutReader
    {
        public LayoutRecord Record { get; } = new();
        public LayoutRecord Read(string projectPath) => Record;
    }
}
