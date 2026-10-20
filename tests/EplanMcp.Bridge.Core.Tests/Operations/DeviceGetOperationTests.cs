using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DeviceGetOperationTests
{
    private const string Path1 = @"D:\Projects\Line1";
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = Path1, Current = true };

    private readonly FakeDeviceReader _devices = new();

    [Fact]
    public void Execute_FullTag_IsReadDirectlyWithoutListingNames()
    {
        Add("=F+A2-KF1", "-KF1");

        DeviceGetData data = Run(new DeviceGetArgs { Tag = "=F+A2-KF1" });

        Assert.Equal("Line1", data.Project);
        Assert.Equal("=F+A2-KF1", data.Device.Tag);
        Assert.Equal((Path1, "=F+A2-KF1", (string?)null, false), Assert.Single(_devices.DeviceReads));
        Assert.Equal(0, _devices.NameReads);
    }

    [Fact]
    public void Execute_UniqueShownTag_FindsTheDeviceByItsFullTag()
    {
        Add("=F+A2-KF1", "-KF1");
        Add("=F+A2-KF2", "-KF2");

        DeviceGetData data = Run(new DeviceGetArgs { Tag = " -KF1 " });

        Assert.Equal("=F+A2-KF1", data.Device.Tag);
        Assert.Equal(["-KF1", "=F+A2-KF1"], _devices.DeviceReads.Select(r => r.Tag));
    }

    [Theory]
    [InlineData("=+-cu1")]
    [InlineData(">=>+>-CU1")]
    public void Execute_ShownTag_IgnoresCaseAndMarkers(string tag)
    {
        Add("+A3-CU1", ">=>+>-CU1");

        Assert.Equal("+A3-CU1", Run(new DeviceGetArgs { Tag = tag }).Device.Tag);
    }

    [Fact]
    public void Execute_ShownTagInSeveralStructures_IsAmbiguousWithTheFullTags()
    {
        Add("=GAA+A1-FC1", "-FC1");
        Add("=GAB1+A1-FC1", "-FC1");
        Add("=GAA+A1-FC2", "-FC2");

        BridgeError error = Fails(new DeviceGetArgs { Tag = "-fc1" });

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
        Assert.EndsWith("To see all of them with their pages in one call, use eplan_device_list with tag_contains.", error.Message, StringComparison.Ordinal);
        Assert.Equal(["=GAA+A1-FC1", "=GAB1+A1-FC1"], error.Metadata!["candidates"].Split('\n'));
        Assert.Equal(("device", "-fc1", "2"), (error.Metadata["kind"], error.Metadata["name"], error.Metadata["candidateCount"]));
        Assert.Contains("full tag", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AmbiguousShownTag_ListsAtMostTwentyCandidates()
    {
        for (int i = 1; i <= 25; i++)
        {
            Add($"=A{i}-K1", "-K1");
        }

        BridgeError error = Fails(new DeviceGetArgs { Tag = "-K1" });

        Assert.Equal(20, error.Metadata!["candidates"].Split('\n').Length);
        Assert.Equal("25", error.Metadata["candidateCount"]);
    }

    [Fact]
    public void Execute_ShownTagOfOneDeviceRepeated_IsThatDevice()
    {
        // A project can repeat a main function's tag; the candidates would all be the same full tag.
        Add("=A1-K1", "-K1");
        _devices.Functions.Add(new FunctionRecord { Name = "=A1-K1", VisibleName = "-K1", IsMain = true });

        Assert.Equal("=A1-K1", Run(new DeviceGetArgs { Tag = "-K1" }).Device.Tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData(">>")]
    public void Execute_DeviceWithoutAShownTag_IsOnlyFoundByItsFullTag(string visibleName)
    {
        Add("=GAA+A1-PF1", visibleName);

        Assert.Equal(ErrorReason.DeviceNotFound, Fails(new DeviceGetArgs { Tag = "-PF1" }).Reason);
        Assert.Equal("=GAA+A1-PF1", Run(new DeviceGetArgs { Tag = "=GAA+A1-PF1" }).Device.Tag);
    }

    [Fact]
    public void Execute_FullTagInAnotherCase_IsNotTheFullTag()
    {
        Add("=GAA+A1-FC1", "-FC1");

        Assert.Equal(ErrorReason.DeviceNotFound, Fails(new DeviceGetArgs { Tag = "=gaa+a1-fc1" }).Reason);
    }

    [Fact]
    public void Execute_UnknownTag_IsDeviceNotFoundPointingAtDeviceList()
    {
        Add("=A1-K1", "-K1");

        BridgeError error = Fails(new DeviceGetArgs { Tag = "-K9" });

        Assert.Equal(ErrorReason.DeviceNotFound, error.Reason);
        Assert.Contains("eplan_device_list", error.Message, StringComparison.Ordinal);
        Assert.Equal("-K9", error.Metadata!["tag"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Execute_NoTag_IsInvalidArgumentBeforeAnythingIsRead(string? tag)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() =>
            new DeviceGetOperation(new FakeCatalog(), _devices).Execute(new DeviceGetArgs { Tag = tag })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'tag'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_devices.DeviceReads);
    }

    [Fact]
    public void Execute_UnknownLanguage_IsTheAdaptersInvalidArgument()
    {
        Add("=A1-K1", "-K1");

        Assert.Equal(ErrorReason.InvalidArgument, Fails(new DeviceGetArgs { Tag = "-K1", Language = "xx_XX" }).Reason);
        Assert.Equal(0, _devices.NameReads);
    }

    [Fact]
    public void Execute_LanguageAndDetail_ReachTheAdapter()
    {
        Add("=A1-K1", "-K1");

        Run(new DeviceGetArgs { Tag = "=A1-K1", Language = " zh_CN ", Detailed = true });
        Run(new DeviceGetArgs { Tag = "=A1-K1", Language = string.Empty });

        Assert.Equal([("zh_CN", true), (null, false)], _devices.DeviceReads.Select(r => (r.Language, r.WithProperties)));
    }

    [Fact]
    public void Execute_MapsTheDevice_ShownTagsWithoutMarkersAndNamedPinsOnly()
    {
        DeviceRecord record = Add("=F+A2-KF1", ">-KF1", category: "Coil", page: "=F+A2&EFS1/1");
        record.Main.Text = "Pump on";
        record.Main.TextLanguage = "en_US";
        record.Main.Pins.AddRange(["A1", string.Empty, "A2"]);
        record.Auxiliary.Add(new FunctionDetail { Name = "=F+A2-KF1", VisibleName = string.Empty, Category = "NoContact", Page = "=F+A2&EFS1/2", Pins = ["13", "14"] });
        record.Parts.Add(new DevicePart { PartNumber = "SIE.3RT2016-1BB41", Variant = "1", Count = 1, Description = "Contactor" });

        DeviceGetData data = Run(new DeviceGetArgs { Tag = "-KF1" });

        Assert.Equal(("-KF1", "Coil", "=F+A2&EFS1/1", "Pump on", "en_US"), (data.Device.VisibleTag, data.Device.Category, data.Device.Page, data.Device.Text, data.Device.TextLanguage));
        Assert.Equal(["A1", "A2"], data.Device.Pins);
        DeviceFunction contact = Assert.Single(data.Functions);
        Assert.Equal(("=F+A2-KF1", null, "NoContact", "=F+A2&EFS1/2"), (contact.Tag, contact.VisibleTag, contact.Category, contact.Page));
        Assert.Equal(["13", "14"], contact.Pins);
        Assert.Equal(1, data.FunctionCount);
        Assert.Equal("SIE.3RT2016-1BB41", Assert.Single(data.Parts).PartNumber);
        Assert.Null(data.Properties);
    }

    [Fact]
    public void Execute_ManyAuxiliaryFunctions_ReturnsTheFirstFiftyAndCountsAll()
    {
        DeviceRecord record = Add("=A1-KF1", "-KF1", category: "PLCBox");
        record.Auxiliary.AddRange(Enumerable.Range(1, 120).Select(i => new FunctionDetail { Name = $"=A1-KF1:{i}", Category = "PLCTerminal" }));

        DeviceGetData data = Run(new DeviceGetArgs { Tag = "=A1-KF1" });

        Assert.Equal(DeviceGetOperation.MaxFunctions, data.Functions.Count);
        Assert.Equal("=A1-KF1:50", data.Functions[^1].Tag);
        Assert.Equal(120, data.FunctionCount);
    }

    [Fact]
    public void Execute_Detailed_ReturnsPropertiesEvenWhenNoneAreSet()
    {
        Add("=A1-K1", "-K1");

        Assert.Empty(Run(new DeviceGetArgs { Tag = "=A1-K1", Detailed = true }).Properties!);
    }

    [Fact]
    public void Execute_UnknownProject_IsProjectNotOpen()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new DeviceGetArgs { Project = "Line9", Tag = "-K1" }).Reason);
        Assert.Empty(_devices.DeviceReads);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        DeviceRecord record = Add("=F+A2-KF1", "-KF1");
        record.Properties = [new PropertyEntry { Id = 20011, Name = "Function text", Value = "Pump" }];
        var router = new OperationRouter(
            [new DeviceGetOperation(new FakeCatalog(_line1), _devices)],
            new InlineDispatcher(),
            null,
            new ListLog(),
            TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.DeviceGet, JObject.Parse("""{"tag":"-KF1","language":"en_US","detailed":true}"""), "c");

        Assert.Equal("-KF1", (string?)data["device"]!["visibleTag"]);
        Assert.Equal(0, (int)data["functionCount"]!);
        Assert.Equal(20011, (int)data["properties"]![0]!["id"]!);
        Assert.Equal("en_US", _devices.DeviceReads[0].Language);
    }

    /// <summary>A main function the fake both lists by name and returns by its full tag.</summary>
    private DeviceRecord Add(string name, string visibleName, string category = "Coil", string? page = null)
    {
        _devices.Functions.Add(new FunctionRecord { Name = name, VisibleName = visibleName, IsMain = true, Category = category, Page = page });
        var record = new DeviceRecord { Main = new FunctionDetail { Name = name, VisibleName = visibleName, Category = category, Page = page } };
        _devices.Devices[name] = record;
        return record;
    }

    private DeviceGetData Run(DeviceGetArgs args) => (DeviceGetData)new DeviceGetOperation(new FakeCatalog(_line1), _devices).Execute(args)!;

    private BridgeError Fails(DeviceGetArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
}
