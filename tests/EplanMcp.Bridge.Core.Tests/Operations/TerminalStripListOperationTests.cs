using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class TerminalStripListOperationTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };

    private readonly FakeTerminalReader _terminals = new();

    public TerminalStripListOperationTests()
    {
        _terminals.Strips.AddRange(
        [
            new TerminalStripListItem { Name = "+A2-XD2", Terminals = 5, Bridges = 3 },
            new TerminalStripListItem { Name = "+A1-XD4", Terminals = 15 },
            new TerminalStripListItem { Name = "+B2.X1-XZ1", Terminals = 33 },
            new TerminalStripListItem { Name = "+A1-xd1", Terminals = 2 },
        ]);
    }

    [Fact]
    public void Execute_ListsEveryStripSortedByNameOrdinal()
    {
        ListData<TerminalStripListItem> data = Run(new TerminalStripListArgs { Limit = 50 });

        Assert.Equal("Line1", data.Project);
        Assert.Equal(["+A1-XD4", "+A1-xd1", "+A2-XD2", "+B2.X1-XZ1"], data.Items.Select(s => s.Name));
        Assert.Equal((5, 3), (data.Items[2].Terminals, data.Items[2].Bridges));
        Assert.Equal(4, data.Total);
        Assert.Equal(_line1.Path, Assert.Single(_terminals.Reads));
    }

    [Fact]
    public void Execute_AStripEplanReturnsTwice_IsListedOnce()
    {
        // The fixture returns 18 of its 27 strips twice, with the same counts (Large test, 2026-10-21).
        _terminals.Strips.Add(new TerminalStripListItem { Name = "+A1-XD4", Terminals = 15 });

        ListData<TerminalStripListItem> data = Run(new TerminalStripListArgs { Limit = 50 });

        Assert.Equal(["+A1-XD4", "+A1-xd1", "+A2-XD2", "+B2.X1-XZ1"], data.Items.Select(s => s.Name));
        Assert.Equal(4, data.Total);
    }

    [Theory]
    [InlineData("-xd", new[] { "+A1-XD4", "+A1-xd1", "+A2-XD2" })]
    [InlineData(" +A1 ", new[] { "+A1-XD4", "+A1-xd1" })]
    [InlineData("", new[] { "+A1-XD4", "+A1-xd1", "+A2-XD2", "+B2.X1-XZ1" })]
    [InlineData("-XK", new string[0])]
    public void Execute_NameContains_FiltersIgnoringCase(string text, string[] expected)
    {
        ListData<TerminalStripListItem> data = Run(new TerminalStripListArgs { NameContains = text, Limit = 50 });

        Assert.Equal(expected, data.Items.Select(s => s.Name));
        Assert.Equal(expected.Length, data.Total);
    }

    [Fact]
    public void Execute_Slices_AndCountsEveryMatch()
    {
        ListData<TerminalStripListItem> data = Run(new TerminalStripListArgs { Offset = 1, Limit = 2 });

        Assert.Equal(["+A1-xd1", "+A2-XD2"], data.Items.Select(s => s.Name));
        Assert.Equal(4, data.Total);
        Assert.Equal(ErrorReason.InvalidArgument, Fails(new TerminalStripListArgs { Offset = 5, Limit = 2 }).Reason);
    }

    [Fact]
    public void Execute_NamedProject_ReadsThatProjectsPathAndAnUnknownOneIsNotOpen()
    {
        Run(new TerminalStripListArgs { Project = "line2", Limit = 50 });

        Assert.Equal(_line2.Path, Assert.Single(_terminals.Reads));
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new TerminalStripListArgs { Project = "Line9", Limit = 50 }).Reason);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        var router = new OperationRouter(
            [new TerminalStripListOperation(new FakeCatalog(_line1), _terminals)],
            new InlineDispatcher(),
            null,
            new ListLog(),
            TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.TerminalStripList, JObject.Parse("""{"nameContains":"+A2","offset":0,"limit":10}"""), "c");

        Assert.Equal(1, (int)data["total"]!);
        Assert.Equal(("+A2-XD2", 5, 3), ((string?)data["items"]![0]!["name"], (int)data["items"]![0]!["terminals"]!, (int)data["items"]![0]!["bridges"]!));
    }

    private ListData<TerminalStripListItem> Run(TerminalStripListArgs args) =>
        (ListData<TerminalStripListItem>)new TerminalStripListOperation(new FakeCatalog(_line1, _line2), _terminals).Execute(args)!;

    private BridgeError Fails(TerminalStripListArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
}
