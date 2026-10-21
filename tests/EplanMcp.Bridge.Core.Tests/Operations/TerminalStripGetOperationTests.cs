using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class TerminalStripGetOperationTests
{
    private const string Path1 = @"D:\Projects\Line1";
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = Path1, Current = true };

    private readonly FakeTerminalReader _terminals = new();

    public TerminalStripGetOperationTests()
    {
        _terminals.Strips.AddRange(
        [
            new TerminalStripListItem { Name = "+A2-XD2", Terminals = 5, Bridges = 3 },
            new TerminalStripListItem { Name = "+A1-XD4", Terminals = 15 },
            new TerminalStripListItem { Name = "+B2.X1-XZ1", Terminals = 33 },
            new TerminalStripListItem { Name = "+A1-XD2", Terminals = 4 },
            new TerminalStripListItem { Name = "X9", Terminals = 1 },
        ]);
        _terminals.Terminals["+A2-XD2"] =
        [
            .. Enumerable.Range(1, 5).Select(i => new TerminalItem { Name = $"+A2-XD2:{i}", Main = i == 1, Page = "=EA+A2&EFS1/1" }),
        ];
    }

    [Fact]
    public void Execute_FullName_ReadsTheStripDirectlyWithoutListingStrips()
    {
        TerminalStripGetData data = Run(new TerminalStripGetArgs { Strip = "+A2-XD2", Limit = 100 });

        Assert.Equal("Line1", data.Project);
        Assert.Equal(("+A2-XD2", 5, 3), (data.Strip.Name, data.Strip.Terminals, data.Strip.Bridges));
        Assert.Equal(["+A2-XD2:1", "+A2-XD2:2", "+A2-XD2:3", "+A2-XD2:4", "+A2-XD2:5"], data.Terminals.Select(t => t.Name));
        Assert.Equal((true, "=EA+A2&EFS1/1"), (data.Terminals[0].Main, data.Terminals[0].Page));
        Assert.Equal(5, data.Total);
        Assert.Equal([(Path1, "+A2-XD2")], _terminals.StripReads);
        Assert.Empty(_terminals.Reads);
    }

    [Theory]
    [InlineData("-xz1", "+B2.X1-XZ1")]
    [InlineData(" -XD4 ", "+A1-XD4")]
    [InlineData("x9", "X9")]
    public void Execute_NameWithoutStructure_FindsTheOneStripWithIt(string name, string fullName)
    {
        Assert.Equal(fullName, Run(new TerminalStripGetArgs { Strip = name, Limit = 100 }).Strip.Name);
    }

    [Fact]
    public void Execute_NameWithoutStructureInSeveralLocations_IsAmbiguousWithTheFullNames()
    {
        BridgeError error = Fails(new TerminalStripGetArgs { Strip = "-XD2", Limit = 100 });

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
        Assert.Equal(["+A2-XD2", "+A1-XD2"], error.Metadata!["candidates"].Split('\n'));
        Assert.Equal(("terminal strip", "-XD2"), (error.Metadata["kind"], error.Metadata["name"]));
        Assert.Contains("as 'strip'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-XD9")]
    [InlineData("+a2-xd2")]
    public void Execute_UnknownName_IsDeviceNotFoundPointingAtTheStripList(string name)
    {
        BridgeError error = Fails(new TerminalStripGetArgs { Strip = name, Limit = 100 });

        Assert.Equal(ErrorReason.DeviceNotFound, error.Reason);
        Assert.Contains("eplan_terminal_strip_list", error.Message, StringComparison.Ordinal);
        Assert.Equal(name, error.Metadata!["strip"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Execute_NoStrip_IsInvalidArgumentBeforeAnythingIsRead(string? strip)
    {
        BridgeError error = Fails(new TerminalStripGetArgs { Strip = strip, Limit = 100 });

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'strip' is required", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_terminals.StripReads);
    }

    [Theory]
    [InlineData("+A2-XD2:1", "1")]
    [InlineData("+A2-XD2:PE.2", "PE.2")]
    [InlineData("=K1+A2-KE1:2.3", "2.3")]
    [InlineData("+A2-XD2:", null)]
    [InlineData("+A2-XD2", null)]
    public void Execute_Designation_IsThePartAfterTheFirstColon(string terminal, string? designation)
    {
        _terminals.Terminals["+A2-XD2"] = [new TerminalItem { Name = terminal }];

        Assert.Equal(designation, Assert.Single(Run(new TerminalStripGetArgs { Strip = "+A2-XD2", Limit = 100 }).Terminals).Designation);
    }

    [Fact]
    public void Execute_Ends_DropUnnamedOnesAndStopAtTenAndBridgesNameOtherTerminalsOnce()
    {
        _terminals.Terminals["+A2-XD2"] =
        [
            new TerminalItem
            {
                Name = "+A2-XD2:1",
                Inside = [new ConnectionEnd { Device = string.Empty, Pin = string.Empty }, .. Enumerable.Range(1, 12).Select(i => new ConnectionEnd { Device = "=EA+A2-QA1", Pin = $"{i}" })],
                Outside = [new ConnectionEnd { Device = "+B1-WG2", Pin = string.Empty }],
                BridgedWith = ["+A2-XD2:2", "+A2-XD2:2", string.Empty, "+A2-XD2:3"],
            },
        ];

        TerminalItem terminal = Assert.Single(Run(new TerminalStripGetArgs { Strip = "+A2-XD2", Limit = 100 }).Terminals);

        Assert.Equal(TerminalStripGetOperation.MaxEnds, terminal.Inside.Count);
        Assert.Equal(("=EA+A2-QA1", "1"), (terminal.Inside[0].Device, terminal.Inside[0].Pin));
        Assert.Equal(("+B1-WG2", (string?)null), (Assert.Single(terminal.Outside).Device, terminal.Outside[0].Pin));
        Assert.Equal(["+A2-XD2:2", "+A2-XD2:3"], terminal.BridgedWith);
    }

    [Fact]
    public void Execute_BridgedWith_KeepsALevelOfTheSameNameAndStopsAtTen()
    {
        // The adapter leaves the terminal itself out; another level of a multi-level terminal can carry its name.
        _terminals.Terminals["+A2-XD2"] =
        [
            new TerminalItem { Name = "+A2-XD2:1", BridgedWith = ["+A2-XD2:1"] },
            new TerminalItem { Name = "+A2-XD2:2", BridgedWith = [.. Enumerable.Range(3, 15).Select(i => $"+A2-XD2:{i}")] },
        ];

        List<TerminalItem> terminals = Run(new TerminalStripGetArgs { Strip = "+A2-XD2", Limit = 100 }).Terminals;

        Assert.Equal(["+A2-XD2:1"], terminals[0].BridgedWith);
        Assert.Equal(TerminalStripGetOperation.MaxEnds, terminals[1].BridgedWith.Count);
        Assert.Equal("+A2-XD2:12", terminals[1].BridgedWith[^1]);
    }

    [Fact]
    public void Execute_Terminals_AreSlicedAndCounted()
    {
        TerminalStripGetData data = Run(new TerminalStripGetArgs { Strip = "+A2-XD2", Offset = 3, Limit = 10 });

        Assert.Equal(["+A2-XD2:4", "+A2-XD2:5"], data.Terminals.Select(t => t.Name));
        Assert.Equal(5, data.Total);
        Assert.Equal(ErrorReason.InvalidArgument, Fails(new TerminalStripGetArgs { Strip = "+A2-XD2", Offset = 6, Limit = 10 }).Reason);
    }

    [Fact]
    public void Execute_UnknownProject_IsProjectNotOpen()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new TerminalStripGetArgs { Project = "Line9", Strip = "+A2-XD2", Limit = 100 }).Reason);
        Assert.Empty(_terminals.StripReads);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        _terminals.Terminals["+A2-XD2"][0].BridgedWith = ["+A2-XD2:2"];
        var router = new OperationRouter(
            [new TerminalStripGetOperation(new FakeCatalog(_line1), _terminals)],
            new InlineDispatcher(),
            null,
            new ListLog(),
            TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.TerminalStripGet, JObject.Parse("""{"strip":"+A2-XD2","offset":0,"limit":2}"""), "c");

        Assert.Equal(3, (int)data["strip"]!["bridges"]!);
        Assert.Equal("1", (string?)data["terminals"]![0]!["designation"]);
        Assert.Equal("+A2-XD2:2", (string?)data["terminals"]![0]!["bridgedWith"]![0]);
        Assert.Equal(5, (int)data["total"]!);
    }

    private TerminalStripGetData Run(TerminalStripGetArgs args) => (TerminalStripGetData)new TerminalStripGetOperation(new FakeCatalog(_line1), _terminals).Execute(args)!;

    private BridgeError Fails(TerminalStripGetArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
}
