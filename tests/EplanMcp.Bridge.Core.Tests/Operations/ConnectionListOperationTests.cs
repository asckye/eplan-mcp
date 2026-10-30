using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionListOperationTests
{
    private const string Path1 = @"D:\Projects\Line1";
    private const string Page1 = "=GAA+A1&EFS1/1";
    private const string Page2 = "=GAA+A1&EFS1/2";
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = Path1, Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };

    private readonly FakeDeviceReader _devices = new();
    private readonly FakeConnectionReader _connections = new();

    public ConnectionListOperationTests()
    {
        _devices.Functions.AddRange(
        [
            Primary("=GAA+A1-FC1", "-FC1"),
            Primary("=GAB1+A1-FC1", "-FC1"),
            Primary("=GAA+A1-XD1", "-XD1"),
            Primary("=GAA+A1-KF1", ">-KF1"),
        ]);
        _connections.Pages.AddRange([Page1, Page2]);
    }

    [Fact]
    public void Execute_Detailed_PreservesConnectionLocatorWithoutGivingOneTo3D()
    {
        var locator = new DrawingLocator { Project = Path1, Page = Page1, Type = 9, Oid = 42, Digest = new string('c', 64) };
        _connections.Connections.Add(new ConnectionRecord { Page = Page1, Locator = locator });
        _connections.Connections.Add(new ConnectionRecord { Page = Page1, Is3D = true, Locator = locator });
        var data = Run(new ConnectionListArgs { Page = Page1, Detailed = true, Limit = 50 });
        Assert.Same(locator, data.Items[0].Locator);
        Assert.Null(data.Items[1].Locator);
    }

    [Fact]
    public void Execute_ByPage_ListsThatPagesConnectionsInEplansOrder()
    {
        _connections.Connections.AddRange(
        [
            Wire("=GAA+A1-FC1", "2", "=GAA+A1-XD1", "1", Page1, crossSection: "1,5", colour: "GY"),
            Wire("=GAA+A1-KF1", "A1", "=GAA+A1-XD1", "2", Page2),
            Wire("=GAA+A1-XD1", "3", "=GAA+A1-FC1:1", "14", Page1),
        ]);

        ListData<ConnectionListItem> data = Run(new ConnectionListArgs { Page = Page1, Limit = 50 });

        Assert.Equal("Line1", data.Project);
        Assert.Equal(2, data.Total);
        ConnectionListItem first = data.Items[0];
        Assert.Equal(("=GAA+A1-FC1", "2"), (first.From!.Device, first.From.Pin));
        Assert.Equal(("=GAA+A1-XD1", "1"), (first.To!.Device, first.To.Pin));
        Assert.Equal((Page1, "1,5", "GY", "2d"), (first.Page, first.CrossSection, first.Colour, first.Kind));
        Assert.Equal("=GAA+A1-FC1:1", data.Items[1].To!.Device);
        Assert.Equal((Path1, (string?)null, Page1), Assert.Single(_connections.Reads));
        Assert.Equal(0, _devices.NameReads);
    }

    [Fact]
    public void Execute_ByFullTag_ListsTheConnectionsOfTheMainAndAuxiliaryFunctions()
    {
        _connections.Connections.AddRange(
        [
            Wire("=GAA+A1-FC1", "2", "=GAA+A1-XD1", "1", Page1),
            Wire("=GAA+A1-KF1", "A1", "=GAA+A1-XD1", "2", Page1),
            Wire("=GAA+A1-XD1", "3", "=GAA+A1-FC1:1", "14", Page2),
        ]);

        ListData<ConnectionListItem> data = Run(new ConnectionListArgs { Device = "=GAA+A1-FC1", Limit = 50 });

        Assert.Equal(2, data.Total);
        Assert.Equal((Path1, "=GAA+A1-FC1", (string?)null), Assert.Single(_connections.Reads));
    }

    [Theory]
    [InlineData("-xd1", "=GAA+A1-XD1")]
    [InlineData("-KF1", "=GAA+A1-KF1")]
    [InlineData(" =GAA+A1-XD1 ", "=GAA+A1-XD1")]
    public void Execute_ByShownTag_ReadsTheConnectionsOfTheOneDeviceShowingIt(string device, string fullTag)
    {
        Run(new ConnectionListArgs { Device = device, Limit = 50 });

        Assert.Equal(fullTag, Assert.Single(_connections.Reads).Device);
    }

    [Fact]
    public void Execute_ShownTagInSeveralStructures_IsAmbiguousAndAsksForTheFullTagAsDevice()
    {
        BridgeError error = Fails(new ConnectionListArgs { Device = "-FC1", Limit = 50 });

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
        Assert.Equal(["=GAA+A1-FC1", "=GAB1+A1-FC1"], error.Metadata!["candidates"].Split('\n'));
        Assert.Contains("as 'device'", error.Message, StringComparison.Ordinal);
        Assert.Empty(_connections.Reads);
    }

    [Theory]
    [InlineData("-K9")]
    [InlineData("=gaa+a1-xd1")]
    public void Execute_UnknownDevice_IsDeviceNotFoundPointingAtDeviceList(string device)
    {
        BridgeError error = Fails(new ConnectionListArgs { Device = device, Page = Page1, Limit = 50 });

        Assert.Equal(ErrorReason.DeviceNotFound, error.Reason);
        Assert.Contains("eplan_device_list", error.Message, StringComparison.Ordinal);
        Assert.Equal(device, error.Metadata!["device"]);
        Assert.Empty(_connections.Reads);
    }

    [Fact]
    public void Execute_DeviceAndPage_ListsTheConnectionsMeetingBoth()
    {
        _connections.Connections.AddRange(
        [
            Wire("=GAA+A1-FC1", "2", "=GAA+A1-XD1", "1", Page1),
            Wire("=GAA+A1-KF1", "A1", "=GAA+A1-XD1", "2", Page1),
            Wire("=GAA+A1-XD1", "3", "=GAA+A1-FC1:1", "14", Page2),
        ]);

        ListData<ConnectionListItem> data = Run(new ConnectionListArgs { Device = "=GAA+A1-XD1", Page = Page2, Limit = 50 });

        Assert.Equal("3", Assert.Single(data.Items).From!.Pin);
        Assert.Equal((Path1, "=GAA+A1-XD1", Page2), Assert.Single(_connections.Reads));
    }

    [Fact]
    public void Execute_UnknownPage_IsPageNotFoundPointingAtPageList()
    {
        BridgeError error = Fails(new ConnectionListArgs { Page = "=GAA+A1&EFS1/9", Limit = 50 });

        Assert.Equal(ErrorReason.PageNotFound, error.Reason);
        Assert.Contains("eplan_page_list", error.Message, StringComparison.Ordinal);
        Assert.Equal(("Line1", "=GAA+A1&EFS1/9"), (error.Metadata!["project"], error.Metadata["page"]));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    public void Execute_NeitherDeviceNorPage_IsInvalidArgumentBeforeAnythingIsRead(string? device, string? page)
    {
        BridgeError error = Fails(new ConnectionListArgs { Device = device, Page = page, Limit = 50 });

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'device' or 'page'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Equal(0, _devices.NameReads);
        Assert.Empty(_connections.Reads);
    }

    [Fact]
    public void Execute_3DConnection_HasKind3dWithoutEndsOrPage()
    {
        ConnectionRecord threeD = Wire("=GAA+A1-FC1", "2", "=GAA+A1-XD1", "1", Page1, crossSection: "1,5", colour: "BU");
        threeD.Is3D = true;
        _connections.Connections.Add(threeD);

        ConnectionListItem item = Assert.Single(Run(new ConnectionListArgs { Page = Page1, Limit = 50 }).Items);

        Assert.Equal(ConnectionListItem.Kind3D, item.Kind);
        Assert.Null(item.From);
        Assert.Null(item.To);
        Assert.Null(item.Page);
        Assert.Equal(("1,5", "BU"), (item.CrossSection, item.Colour));
    }

    [Fact]
    public void Execute_EmptyNames_AreNullAndAnEndWithoutEitherIsNotConnected()
    {
        _connections.Connections.Add(Wire(string.Empty, string.Empty, "=GAA+A1-XD1", string.Empty, Page1));
        _connections.Connections.Add(Wire(null, null, string.Empty, "PE", Page1));

        ListData<ConnectionListItem> data = Run(new ConnectionListArgs { Page = Page1, Limit = 50 });

        Assert.Null(data.Items[0].From);
        Assert.Equal(("=GAA+A1-XD1", (string?)null), (data.Items[0].To!.Device, data.Items[0].To!.Pin));
        Assert.Null(data.Items[1].From);
        Assert.Equal(((string?)null, "PE"), (data.Items[1].To!.Device, data.Items[1].To!.Pin));
    }

    [Fact]
    public void Execute_Slices_AndCountsEveryMatch()
    {
        _connections.Connections.AddRange(Enumerable.Range(1, 7).Select(i => Wire("=GAA+A1-XD1", $"{i}", "=GAA+A1-FC1", "1", Page1)));

        ListData<ConnectionListItem> data = Run(new ConnectionListArgs { Page = Page1, Offset = 2, Limit = 3 });

        Assert.Equal(["3", "4", "5"], data.Items.Select(c => c.From!.Pin));
        Assert.Equal(7, data.Total);
    }

    [Fact]
    public void Execute_OffsetPastTheMatches_IsInvalidArgument()
    {
        Assert.Equal(ErrorReason.InvalidArgument, Fails(new ConnectionListArgs { Page = Page1, Offset = 5, Limit = 5 }).Reason);
    }

    [Fact]
    public void Execute_NamedProject_ReadsThatProjectsPath()
    {
        Run(new ConnectionListArgs { Project = "line2", Page = Page1, Limit = 50 });

        Assert.Equal(_line2.Path, Assert.Single(_connections.Reads).Path);
    }

    [Fact]
    public void Execute_UnknownProject_IsProjectNotOpen()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new ConnectionListArgs { Project = "Line9", Device = "-XD1", Limit = 50 }).Reason);
        Assert.Empty(_connections.Reads);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        _connections.Connections.Add(Wire("=GAA+A1-FC1", "2", "=GAA+A1-XD1", "1", Page1, crossSection: "2,5"));
        var router = new OperationRouter(
            [new ConnectionListOperation(new FakeCatalog(_line1), _devices, _connections)],
            new InlineDispatcher(),
            null,
            new ListLog(),
            TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.ConnectionList, JObject.Parse("""{"device":"-XD1","page":"=GAA+A1&EFS1/1","offset":0,"limit":10}"""), "c");

        Assert.Equal(1, (int)data["total"]!);
        JToken item = data["items"]![0]!;
        Assert.Equal("=GAA+A1-FC1", (string?)item["from"]!["device"]);
        Assert.Equal("2,5", (string?)item["crossSection"]);
        Assert.Equal("2d", (string?)item["kind"]);
    }

    private static FunctionRecord Primary(string name, string visibleName) => new() { Name = name, VisibleName = visibleName, IsMain = true };

    private static ConnectionRecord Wire(string? startDevice, string? startPin, string? endDevice, string? endPin, string? page, string? crossSection = null, string? colour = null) =>
        new() { StartDevice = startDevice, StartPin = startPin, EndDevice = endDevice, EndPin = endPin, Page = page, CrossSection = crossSection, Colour = colour };

    private ListData<ConnectionListItem> Run(ConnectionListArgs args) =>
        (ListData<ConnectionListItem>)new ConnectionListOperation(new FakeCatalog(_line1, _line2), _devices, _connections).Execute(args)!;

    private BridgeError Fails(ConnectionListArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
}
