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

    /// <summary>A PLC box the fake both lists by name and returns by its full tag.</summary>
    private void Plc(string name, string visibleName, params PlcChannel[] channels)
    {
        _devices.Functions.Add(new FunctionRecord { Name = name, VisibleName = visibleName, IsMain = true, Category = "PLCBox" });
        _plcs.Records[name] = new PlcRecord
        {
            Category = "PLCBox",
            Plc = new PlcInfo { Tag = name, VisibleTag = visibleName, Parts = ["PXC.2703994"], Station = "S1" },
            Channels = [.. channels],
        };
    }

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
