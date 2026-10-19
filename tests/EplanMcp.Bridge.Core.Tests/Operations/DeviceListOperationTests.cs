using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DeviceListOperationTests
{
    private const string Path1 = @"D:\Projects\Line1";
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = Path1, Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };

    private readonly FakeDeviceReader _devices = new();

    [Fact]
    public void Execute_ByDefault_ListsNamedMainFunctionsInEplansOrder()
    {
        _devices.Functions.AddRange(
        [
            Primary("=GAA+A1-FC1", "-FC1"),
            Auxiliary("=GAA+A1-FC1", "-FC1"),
            Primary(string.Empty, string.Empty),
            Primary("+A1-FC2", "+A1-FC2", category: "TerminalDefText"),
        ]);

        ListData<DeviceListItem> data = Run(new DeviceListArgs { Limit = 50 });

        Assert.Equal("Line1", data.Project);
        Assert.Equal(2, data.Total);
        Assert.Equal(["=GAA+A1-FC1", "+A1-FC2"], data.Items.Select(d => d.Tag));
        Assert.All(data.Items, d => Assert.True(d.Main));
        Assert.Equal((Path1, (string?)null, (string?)null), Assert.Single(_devices.Reads));
    }

    [Fact]
    public void Execute_IncludeAuxiliary_ListsAuxiliaryFunctionsMarkedAsSuch()
    {
        _devices.Functions.AddRange([Primary("=F+A2-KF1", "-KF1", category: "Coil"), Auxiliary("=F+A2-KF1", "-KF1", category: "NoContact"), Auxiliary(string.Empty, "-X")]);

        ListData<DeviceListItem> data = Run(new DeviceListArgs { IncludeAuxiliary = true, Limit = 50 });

        Assert.Equal([("Coil", true), ("NoContact", false)], data.Items.Select(d => (d.Category, d.Main)));
    }

    [Theory]
    [InlineData(true, new[] { "-K1", "-K3" })]
    [InlineData(false, new[] { "-K2" })]
    public void Execute_HasPart_KeepsOnlyFunctionsWithOrWithoutParts(bool hasPart, string[] expected)
    {
        _devices.Functions.AddRange([Primary("-K1", "-K1", parts: 1), Primary("-K2", "-K2"), Primary("-K3", "-K3", parts: 3)]);

        ListData<DeviceListItem> data = Run(new DeviceListArgs { HasPart = hasPart, Limit = 50 });

        Assert.Equal(expected, data.Items.Select(d => d.Tag));
    }

    [Theory]
    [InlineData("-fc1", new[] { "=GAA+A1-FC1", "=GAB1+A1-FC1" })]
    [InlineData("=gab1", new[] { "=GAB1+A1-FC1" })]
    [InlineData("-Q9", new[] { "=AP+ST1-K5" })]
    [InlineData("=+-CU1", new[] { "+A9-CU1" })]
    [InlineData(">", new string[0])]
    public void Execute_TagContains_MatchesTheFullOrTheShownTagIgnoringCase(string text, string[] expected)
    {
        _devices.Functions.AddRange(
        [
            Primary("=GAA+A1-FC1", "-FC1"),
            Primary("=GAB1+A1-FC1", "-FC1"),
            Primary("=AP+ST1-K5", "-Q9"),
            Primary("+A9-CU1", ">=>+>-CU1"),
        ]);

        ListData<DeviceListItem> data = Run(new DeviceListArgs { TagContains = text, Limit = 50 });

        Assert.Equal(expected, data.Items.Select(d => d.Tag));
    }

    [Fact]
    public void Execute_VisibleTag_LosesItsMarkersAndIsNullWhenEmpty()
    {
        _devices.Functions.AddRange([Primary("=+-CU1", ">=>+>-CU1"), Primary("=GAA+A1-PF1", string.Empty), Primary("=GAA+A1-PF2", ">>")]);

        ListData<DeviceListItem> data = Run(new DeviceListArgs { Limit = 50 });

        Assert.Equal(["=+-CU1", null, null], data.Items.Select(d => d.VisibleTag));
    }

    [Fact]
    public void Execute_Filters_CombineWithAnd()
    {
        _devices.Functions.AddRange(
        [
            Primary("=A1-M1", "-M1", category: "Motor", page: "=A1&EFS1/1"),
            Primary("=A1-M2", "-M2", category: "Motor", page: "=A1&EFS1/1", parts: 1),
            Primary("=A2-M1", "-M1", category: "Motor", page: "=A1&EFS1/2"),
            Primary("=A1-K1", "-K1", category: "Coil", page: "=A1&EFS1/1"),
        ]);

        ListData<DeviceListItem> data = Run(new DeviceListArgs { Page = " =A1&EFS1/1 ", Category = "motor", HasPart = false, TagContains = "-m", Limit = 50 });

        Assert.Equal("=A1-M1", Assert.Single(data.Items).Tag);
        Assert.Equal((Path1, "=A1&EFS1/1", "motor"), Assert.Single(_devices.Reads));
    }

    [Fact]
    public void Execute_BlankFilters_AreNoFilters()
    {
        _devices.Functions.Add(Primary("-K1", "-K1"));

        ListData<DeviceListItem> data = Run(new DeviceListArgs { Page = string.Empty, Category = "  ", TagContains = string.Empty, Limit = 50 });

        Assert.Single(data.Items);
        Assert.Equal((Path1, (string?)null, (string?)null), Assert.Single(_devices.Reads));
    }

    [Fact]
    public void Execute_UnknownPage_IsPageNotFoundPointingAtPageList()
    {
        _devices.Functions.Add(Primary("-K1", "-K1", page: "=A1&EFS1/1"));

        BridgeError error = Fails(new DeviceListArgs { Page = "=A1&EFS1/9", Limit = 50 });

        Assert.Equal(ErrorReason.PageNotFound, error.Reason);
        Assert.Contains("eplan_page_list", error.Message, StringComparison.Ordinal);
        Assert.Equal("=A1&EFS1/9", error.Metadata!["page"]);
    }

    [Fact]
    public void Execute_UnknownCategory_IsInvalidArgumentWithCommonValuesBeforeAnythingIsRead()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() =>
            new DeviceListOperation(new FakeCatalog(), _devices).Execute(new DeviceListArgs { Category = "Contactor", Limit = 50 })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'category'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Contains("Coil, NoContact", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Empty(_devices.Reads);
    }

    [Fact]
    public void Execute_Slices_AndCountsEveryMatch()
    {
        _devices.Functions.AddRange(Enumerable.Range(1, 7).Select(i => Primary($"-K{i}", $"-K{i}")));
        _devices.Functions.Insert(2, Primary(string.Empty, "-X"));

        ListData<DeviceListItem> data = Run(new DeviceListArgs { Offset = 2, Limit = 3 });

        Assert.Equal(["-K3", "-K4", "-K5"], data.Items.Select(d => d.Tag));
        Assert.Equal(7, data.Total);
    }

    [Fact]
    public void Execute_OffsetPastTheMatches_IsInvalidArgument()
    {
        _devices.Functions.Add(Primary("-K1", "-K1"));

        Assert.Equal(ErrorReason.InvalidArgument, Fails(new DeviceListArgs { Offset = 5, Limit = 5 }).Reason);
    }

    [Fact]
    public void Execute_NamedProject_ReadsThatProjectsPath()
    {
        Run(new DeviceListArgs { Project = "line2", Limit = 50 });

        Assert.Equal(_line2.Path, Assert.Single(_devices.Reads).Path);
    }

    [Fact]
    public void Execute_UnknownProject_IsProjectNotOpen()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new DeviceListArgs { Project = "Line9", Limit = 50 }).Reason);
        Assert.Empty(_devices.Reads);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        _devices.Functions.AddRange([Primary("=A1-K1", "-K1", parts: 1), Auxiliary("=A1-K1", "-K1", parts: 1), Primary("=A1-K2", "-K2")]);
        var router = new OperationRouter(
            [new DeviceListOperation(new FakeCatalog(_line1), _devices)],
            new InlineDispatcher(),
            null,
            new ListLog(),
            TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(
            OperationNames.DeviceList,
            JObject.Parse("""{"tagContains":"k1","hasPart":true,"includeAuxiliary":true,"offset":0,"limit":10}"""),
            "c");

        Assert.Equal(2, (int)data["total"]!);
        Assert.Equal("-K1", (string?)data["items"]![0]!["visibleTag"]);
        Assert.False((bool)data["items"]![1]!["main"]!);
    }

    private static FunctionRecord Primary(string name, string visibleName, string category = "CircuitBreaker", string? page = null, int parts = 0) =>
        new() { Name = name, VisibleName = visibleName, IsMain = true, Category = category, Page = page, Parts = parts };

    private static FunctionRecord Auxiliary(string name, string visibleName, string category = "NoContact", string? page = null, int parts = 0) =>
        new() { Name = name, VisibleName = visibleName, IsMain = false, Category = category, Page = page, Parts = parts };

    private ListData<DeviceListItem> Run(DeviceListArgs args) =>
        (ListData<DeviceListItem>)new DeviceListOperation(new FakeCatalog(_line1, _line2), _devices).Execute(args)!;

    private BridgeError Fails(DeviceListArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
}
