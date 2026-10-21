using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class CableGetOperationTests
{
    private const string Path1 = @"D:\Projects\Line1";
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = Path1, Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };

    private readonly FakeDeviceReader _devices = new();
    private readonly FakeCableReader _cables = new();

    [Fact]
    public void Execute_FullTag_ReadsTheCableDirectlyWithoutListingNames()
    {
        Cable("+B1-WG2", "-WG2", Core("=F+B1-SG1", "1", "+A2-XG4", "2", "433"), Core("=F+B1-SG1", "2", "+A2-XG4", "3", null));

        CableGetData data = Run(new CableGetArgs { Cable = "+B1-WG2", Limit = 100 });

        Assert.Equal("Line1", data.Project);
        Assert.Equal(("+B1-WG2", "-WG2", "ÖLFLEX CLASSIC 110", "3", "1,5", "5 m"), (data.Cable.Tag, data.Cable.VisibleTag, data.Cable.Type, data.Cable.WireCount, data.Cable.CrossSection, data.Cable.Length));
        Assert.Equal(2, data.Total);
        ConnectionListItem first = data.Cores[0];
        Assert.Equal(("=F+B1-SG1", "1", "+A2-XG4", "2"), (first.From!.Device, first.From.Pin, first.To!.Device, first.To.Pin));
        Assert.Equal(("433", "BK", "1,5", "=F+A2&EFS1/2", "2d"), (first.Designation, first.Colour, first.CrossSection, first.Page, first.Kind));
        Assert.Null(data.Cores[1].Designation);
        Assert.Equal([(Path1, "+B1-WG2", (string?)null)], _cables.Reads);
        Assert.Equal(0, _devices.NameReads);
    }

    [Theory]
    [InlineData("-wg2")]
    [InlineData(" >-WG2 ")]
    public void Execute_UniqueShownTag_FindsTheCableByItsFullTag(string tag)
    {
        Cable("+B1-WG2", ">-WG2");

        CableGetData data = Run(new CableGetArgs { Cable = tag, Limit = 100 });

        Assert.Equal(("+B1-WG2", "-WG2"), (data.Cable.Tag, data.Cable.VisibleTag));
        Assert.Equal("+B1-WG2", _cables.Reads[^1].Tag);
    }

    [Fact]
    public void Execute_ShownTagInSeveralStructures_IsAmbiguousAskingForTheFullTagAsCable()
    {
        Cable("+B1-WG2", "-WG2");
        Cable("+B2-WG2", "-WG2");

        BridgeError error = Fails(new CableGetArgs { Cable = "-WG2", Limit = 100 });

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
        Assert.Equal(["+B1-WG2", "+B2-WG2"], error.Metadata!["candidates"].Split('\n'));
        Assert.Contains("as 'cable'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownTag_IsDeviceNotFoundPointingAtTheCableCategory()
    {
        BridgeError error = Fails(new CableGetArgs { Cable = "-WG9", Limit = 100 });

        Assert.Equal(ErrorReason.DeviceNotFound, error.Reason);
        Assert.Contains("eplan_device_list with category Cable", error.Message, StringComparison.Ordinal);
        Assert.Equal("-WG9", error.Metadata!["cable"]);
    }

    [Theory]
    [InlineData("Coil")]
    [InlineData("Shielding")]
    public void Execute_DeviceThatIsNoCable_IsInvalidArgumentNamingItsCategory(string category)
    {
        _devices.Functions.Add(new FunctionRecord { Name = "=A1-KF1", VisibleName = "-KF1", IsMain = true, Category = category });
        _cables.Records["=A1-KF1"] = new CableRecord { Category = category };

        BridgeError error = Fails(new CableGetArgs { Cable = "-KF1", Limit = 100 });

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(category, error.Metadata!["category"]);
        Assert.Contains("eplan_device_get", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Contains($"category {category}, not a cable", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Execute_NoCable_IsInvalidArgumentBeforeAnythingIsRead(string? cable)
    {
        BridgeError error = Fails(new CableGetArgs { Cable = cable, Limit = 100 });

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'cable' is required", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_cables.Reads);
    }

    [Fact]
    public void Execute_SourcesAndTargets_AreNamedOnceEachAndAtMostTwenty()
    {
        CableRecord record = Cable("+B1-WG2", "-WG2");
        record.Cable!.Sources = ["=F+B1-SG1", "=F+B1-SG1", "=F+B1-SG2"];
        record.Cable.Targets = [.. Enumerable.Range(1, 30).Select(i => $"+A2-XG4:{i}")];

        CableInfo cable = Run(new CableGetArgs { Cable = "+B1-WG2", Limit = 100 }).Cable;

        Assert.Equal(["=F+B1-SG1", "=F+B1-SG2"], cable.Sources);
        Assert.Equal(CableGetOperation.MaxEnds, cable.Targets.Count);
        Assert.Equal("+A2-XG4:20", cable.Targets[^1]);
    }

    [Fact]
    public void Execute_Cores_AreSlicedAndCountedAndA3DCoreHasNoEnds()
    {
        CableRecord record = Cable("+B1-WG2", "-WG2", [.. Enumerable.Range(1, 5).Select(i => Core("=F+B1-SG1", $"{i}", "+A2-XG4", $"{i}", $"W{i}"))]);
        record.Cores[3].Is3D = true;

        CableGetData data = Run(new CableGetArgs { Cable = "+B1-WG2", Offset = 2, Limit = 2 });

        Assert.Equal(["W3", "W4"], data.Cores.Select(c => c.Designation));
        Assert.Equal(5, data.Total);
        Assert.Equal(ConnectionListItem.Kind3D, data.Cores[1].Kind);
        Assert.Null(data.Cores[1].From);
        Assert.Null(data.Cores[1].Page);
        Assert.Equal(ErrorReason.InvalidArgument, Fails(new CableGetArgs { Cable = "+B1-WG2", Offset = 6, Limit = 2 }).Reason);
    }

    [Fact]
    public void Execute_LanguageAndProject_ReachTheReader()
    {
        Cable("+B1-WG2", "-WG2");

        Run(new CableGetArgs { Project = "line2", Cable = "+B1-WG2", Language = " de_DE ", Limit = 100 });

        Assert.Equal((_line2.Path, "+B1-WG2", "de_DE"), Assert.Single(_cables.Reads));
    }

    [Fact]
    public void Execute_UnknownProject_IsProjectNotOpen()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new CableGetArgs { Project = "Line9", Cable = "-WG2", Limit = 100 }).Reason);
        Assert.Empty(_cables.Reads);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        Cable("+B1-WG2", "-WG2", Core("=F+B1-SG1", "1", "+A2-XG4", "2", "433"));
        var router = new OperationRouter(
            [new CableGetOperation(new FakeCatalog(_line1), _devices, _cables)],
            new InlineDispatcher(),
            null,
            new ListLog(),
            TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.CableGet, JObject.Parse("""{"cable":"-WG2","language":"en_US","offset":0,"limit":10}"""), "c");

        Assert.Equal("-WG2", (string?)data["cable"]!["visibleTag"]);
        Assert.Equal("1,5", (string?)data["cable"]!["crossSection"]);
        Assert.Equal("433", (string?)data["cores"]![0]!["designation"]);
        Assert.Equal(1, (int)data["total"]!);
        Assert.Equal("en_US", _cables.Reads[^1].Language);
    }

    private static ConnectionRecord Core(string start, string startPin, string end, string endPin, string? designation) => new()
    {
        StartDevice = start,
        StartPin = startPin,
        EndDevice = end,
        EndPin = endPin,
        Page = "=F+A2&EFS1/2",
        CrossSection = "1,5",
        Colour = "BK",
        Designation = designation,
    };

    /// <summary>A cable the fake both lists by name and returns by its full tag.</summary>
    private CableRecord Cable(string name, string visibleName, params ConnectionRecord[] cores)
    {
        _devices.Functions.Add(new FunctionRecord { Name = name, VisibleName = visibleName, IsMain = true, Category = "Cable" });
        var record = new CableRecord
        {
            Category = "Cable",
            Cable = new CableInfo { Tag = name, VisibleTag = visibleName, Type = "ÖLFLEX CLASSIC 110", WireCount = "3", CrossSection = "1,5", Length = "5 m" },
            Cores = [.. cores],
        };
        _cables.Records[name] = record;
        return record;
    }

    private CableGetData Run(CableGetArgs args) => (CableGetData)new CableGetOperation(new FakeCatalog(_line1, _line2), _devices, _cables).Execute(args)!;

    private BridgeError Fails(CableGetArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;

    /// <summary>Returns <see cref="Records"/> by full tag (ordinal), like the adapter's exact lookup; records every read.</summary>
    private sealed class FakeCableReader : ICableReader
    {
        public Dictionary<string, CableRecord> Records { get; } = new(StringComparer.Ordinal);

        public List<(string Path, string Tag, string? Language)> Reads { get; } = [];

        public CableRecord? ReadCable(string projectPath, string tag, string? language)
        {
            Reads.Add((projectPath, tag, language));
            return Records.GetValueOrDefault(tag);
        }
    }
}
