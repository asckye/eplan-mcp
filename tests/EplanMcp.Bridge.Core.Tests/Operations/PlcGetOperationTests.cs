using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlcGetOperationTests
{
    private const string Path1 = @"D:\Projects\Line1";
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = Path1, Current = true };

    private readonly FakeDeviceReader _devices = new();
    private readonly FakePlcReader _plcs = new();

    [Fact]
    public void Execute_FullTag_ReadsThePlcBoxDirectlyWithItsChannels()
    {
        Plc("=K1+A2-KE1", "-KE1", Channel("=K1+A2-KE1:2.3", "I0.3", ("=K1+A2-SF1", "13")), Channel("=K1+A2-KE1:2.4", "I0.4"));

        PlcGetData data = Run(new PlcGetArgs { Plc = "=K1+A2-KE1", Limit = 100 });

        Assert.Equal("Line1", data.Project);
        Assert.Equal(("=K1+A2-KE1", "-KE1", "PXC.2703994", "S1", false), (data.Plc.Tag, data.Plc.VisibleTag, Assert.Single(data.Plc.Parts), data.Plc.Station, data.Plc.Cpu));
        Assert.Equal(["=K1+A2-KE1:2.3", "=K1+A2-KE1:2.4"], data.Channels.Select(c => c.Name));
        Assert.Equal(("I0.3", "BOOL"), (data.Channels[0].Address, data.Channels[0].DataType));
        Assert.Equal(("=K1+A2-SF1", "13"), (Assert.Single(data.Channels[0].Connected).Device, data.Channels[0].Connected[0].Pin));
        Assert.Empty(data.Channels[1].Connected);
        Assert.Equal((2, 1), (data.Total, data.Connected));
        Assert.Equal([(Path1, "=K1+A2-KE1", (string?)null)], _plcs.Reads);
        Assert.Equal(0, _devices.NameReads);
    }

    [Fact]
    public void Execute_UniqueShownTag_FindsThePlcBoxByItsFullTag()
    {
        Plc("=K1+A2-KE1", ">-KE1");

        PlcGetData data = Run(new PlcGetArgs { Plc = "-ke1", Limit = 100 });

        Assert.Equal(("=K1+A2-KE1", "-KE1"), (data.Plc.Tag, data.Plc.VisibleTag));
    }

    [Fact]
    public void Execute_ShownTagInSeveralStructures_IsAmbiguousAskingForTheFullTagAsPlc()
    {
        Plc("=K1+A2-KE1", "-KE1");
        Plc("=K2+A2-KE1", "-KE1");

        BridgeError error = Fails(new PlcGetArgs { Plc = "-KE1", Limit = 100 });

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
        Assert.Equal(["=K1+A2-KE1", "=K2+A2-KE1"], error.Metadata!["candidates"].Split('\n'));
        Assert.Contains("as 'plc'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownTag_IsDeviceNotFoundPointingAtThePlcBoxCategory()
    {
        BridgeError error = Fails(new PlcGetArgs { Plc = "-KE9", Limit = 100 });

        Assert.Equal(ErrorReason.DeviceNotFound, error.Reason);
        Assert.Contains("eplan_device_list with category PLCBox", error.Message, StringComparison.Ordinal);
        Assert.Equal("-KE9", error.Metadata!["plc"]);
    }

    [Fact]
    public void Execute_DeviceThatIsNoPlcBox_IsInvalidArgumentNamingItsCategory()
    {
        _devices.Functions.Add(new FunctionRecord { Name = "=K1+A2-SF1", VisibleName = "-SF1", IsMain = true, Category = "Switch" });
        _plcs.Records["=K1+A2-SF1"] = new PlcRecord { Category = "Switch" };

        BridgeError error = Fails(new PlcGetArgs { Plc = "-SF1", Limit = 100 });

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal("Switch", error.Metadata!["category"]);
        Assert.Contains("category Switch, not a PLC box", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Execute_NoPlc_IsInvalidArgumentBeforeAnythingIsRead(string? plc)
    {
        BridgeError error = Fails(new PlcGetArgs { Plc = plc, Limit = 100 });

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'plc' is required", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_plcs.Reads);
    }

    [Fact]
    public void Execute_Connections_DropUnnamedEndsStopAtTenAndCountOverEveryChannel()
    {
        PlcChannel crowded = Channel("=K1+A2-KE1:1", null, [.. Enumerable.Range(1, 12).Select(i => ("=K1+A2-SF" + i, "13"))]);
        crowded.Connected.Insert(0, new ConnectionEnd { Device = string.Empty, Pin = string.Empty });
        PlcChannel unnamed = Channel("=K1+A2-KE1:2", null, (string.Empty, string.Empty));
        Plc("=K1+A2-KE1", "-KE1", [crowded, unnamed, .. Enumerable.Range(3, 3).Select(i => Channel($"=K1+A2-KE1:{i}", null, ("-X1", $"{i}")))]);

        PlcGetData data = Run(new PlcGetArgs { Plc = "=K1+A2-KE1", Offset = 0, Limit = 2 });

        Assert.Equal(PlcGetOperation.MaxEnds, data.Channels[0].Connected.Count);
        Assert.Equal("=K1+A2-SF1", data.Channels[0].Connected[0].Device);
        Assert.Empty(data.Channels[1].Connected);
        Assert.Equal((5, 4), (data.Total, data.Connected));
        Assert.Equal(ErrorReason.InvalidArgument, Fails(new PlcGetArgs { Plc = "=K1+A2-KE1", Offset = 6, Limit = 2 }).Reason);
    }

    [Fact]
    public void Execute_ChannelsOfOneName_AreAllKeptInEplansOrderWithTheirPageAndMain()
    {
        // The fixture's =K1+A2-KE1: 34 channel objects with 17 names, e.g. :1.1 as the output %QX2.0 and the input %IX2.0.
        Plc("=K1+A2-KE1", "-KE1", [.. Pair("1.1", "%QX2.0", "%IX2.0"), .. Pair("1.2", "%IX2.1", null)]);

        PlcGetData data = Run(new PlcGetArgs { Plc = "=K1+A2-KE1", Limit = 100 });

        Assert.Equal(["=K1+A2-KE1:1.1", "=K1+A2-KE1:1.1", "=K1+A2-KE1:1.2", "=K1+A2-KE1:1.2"], data.Channels.Select(c => c.Name));
        Assert.Equal(["%QX2.0", "%IX2.0", "%IX2.1", null], data.Channels.Select(c => c.Address));
        Assert.Equal([("=K1+A2&EFS1/3", true), ("=K1+A2&EFS1/1", false)], data.Channels.Take(2).Select(c => (c.Page, c.Main)));
        Assert.Equal((4, 0), (data.Total, data.Connected));
    }

    [Fact]
    public void Execute_ConnectionEnds_GoToTheirChannelObjectElseToTheChannelsOfThatNameOnTheConnectionsPage()
    {
        Plc("=K1+A2-KE1", "-KE1", [.. Pair("1.1", "%QX2.0", "%IX2.0"), .. Pair("1.2", "%IX2.1", null)]).Connections.AddRange(
        [
            Wire("=K1+A2-KE1:1.2", "=K1+A2&EFS1/3", "=K1+A2-FC2:2", "2"),
            Wire("=K1+A2-KE1:1.1", "=K1+A2&EFS1/9", "+A2-XD4:3", "1"),
            Wire("=K1+A2-KE1:1.1", "=K1+A2&EFS1/1", "-X9", "1", channel: 0),
            Wire("=K1+A2-KE1", "=K1+A2&EFS1/3", "=K1+A2-GB1", "1"),
            Wire("=K1+A2-KE1:1.2", "=K1+A2&EFS1/1", string.Empty, string.Empty),
        ]);

        List<PlcChannel> channels = Run(new PlcGetArgs { Plc = "=K1+A2-KE1", Limit = 100 }).Channels;

        // Object first; by name, the one on the connection's page, or every one of the name when none is on it.
        Assert.Equal(["+A2-XD4:3", "-X9"], channels[0].Connected.Select(e => e.Device));
        Assert.Equal(["+A2-XD4:3"], channels[1].Connected.Select(e => e.Device));
        Assert.Equal([("=K1+A2-FC2:2", "2")], channels[2].Connected.Select(e => (e.Device, e.Pin)));
        Assert.Empty(channels[3].Connected);
    }

    [Fact]
    public void Execute_Language_ReachesTheReader()
    {
        Plc("=K1+A2-KE1", "-KE1");

        Run(new PlcGetArgs { Plc = "=K1+A2-KE1", Language = " en_US ", Limit = 100 });

        Assert.Equal("en_US", Assert.Single(_plcs.Reads).Language);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        Plc("=K1+A2-KE1", "-KE1", Channel("=K1+A2-KE1:2.3", "I0.3", ("=K1+A2-SF1", "13")));
        var router = new OperationRouter(
            [new PlcGetOperation(new FakeCatalog(_line1), _devices, _plcs)],
            new InlineDispatcher(),
            null,
            new ListLog(),
            TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.PlcGet, JObject.Parse("""{"plc":"-KE1","offset":0,"limit":10}"""), "c");

        Assert.Equal("-KE1", (string?)data["plc"]!["visibleTag"]);
        Assert.Equal("I0.3", (string?)data["channels"]![0]!["address"]);
        Assert.Equal("13", (string?)data["channels"]![0]!["connected"]![0]!["pin"]);
        Assert.Equal((1, 1), ((int)data["total"]!, (int)data["connected"]!));
    }

    private static PlcChannel Channel(string name, string? address, params (string Device, string Pin)[] ends) => new()
    {
        Name = name,
        Address = address,
        DataType = "BOOL",
        Connected = [.. ends.Select(e => new ConnectionEnd { Device = e.Device, Pin = e.Pin })],
    };

    /// <summary>
    /// A PLC box the fake both lists by name and returns by its full tag. Each channel's ends become connections the
    /// reader matched to that channel object, as the adapter gives them; the channels themselves carry none.
    /// </summary>
    private PlcRecord Plc(string name, string visibleName, params PlcChannel[] channels)
    {
        _devices.Functions.Add(new FunctionRecord { Name = name, VisibleName = visibleName, IsMain = true, Category = "PLCBox" });
        var record = new PlcRecord
        {
            Category = "PLCBox",
            Plc = new PlcInfo { Tag = name, VisibleTag = visibleName, Parts = ["PXC.2703994"], Station = "S1" },
            Channels = [.. channels],
            Connections =
            [
                .. channels.SelectMany((channel, i) => channel.Connected.Select(end => new PlcConnectionRecord { Channel = i, Function = channel.Name, Other = end })),
            ],
        };
        channels.ToList().ForEach(channel => channel.Connected = []);
        _plcs.Records[name] = record;
        return record;
    }

    /// <summary>Two channel objects of one name: the main one on page 3, an auxiliary one on page 1.</summary>
    private static PlcChannel[] Pair(string point, string? mainAddress, string? auxiliaryAddress) =>
    [
        new() { Name = "=K1+A2-KE1:" + point, Address = mainAddress, Page = "=K1+A2&EFS1/3", Main = true },
        new() { Name = "=K1+A2-KE1:" + point, Address = auxiliaryAddress, Page = "=K1+A2&EFS1/1" },
    ];

    private static PlcConnectionRecord Wire(string function, string? page, string device, string pin, int? channel = null) =>
        new() { Channel = channel, Function = function, Page = page, Other = new ConnectionEnd { Device = device, Pin = pin } };

    private PlcGetData Run(PlcGetArgs args) => (PlcGetData)new PlcGetOperation(new FakeCatalog(_line1), _devices, _plcs).Execute(args)!;

    private BridgeError Fails(PlcGetArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;

    /// <summary>Returns <see cref="Records"/> by full tag (ordinal), like the adapter's exact lookup; records every read.</summary>
    private sealed class FakePlcReader : IPlcReader
    {
        public Dictionary<string, PlcRecord> Records { get; } = new(StringComparer.Ordinal);

        public List<(string Path, string Tag, string? Language)> Reads { get; } = [];

        public PlcRecord? ReadPlc(string projectPath, string tag, string? language)
        {
            Reads.Add((projectPath, tag, language));
            return Records.GetValueOrDefault(tag);
        }
    }
}
