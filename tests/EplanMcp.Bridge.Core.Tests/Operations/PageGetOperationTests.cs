using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageGetOperationTests
{
    private const string Circuit = "=GAA+A1&EFS1/1";

    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };

    private readonly FakePageReader _reader = new();

    public PageGetOperationTests()
    {
        _reader.Pages.AddRange(
        [
            new PageSummary { Name = "&AAA1/1", Type = "TitlePage" },
            new PageSummary { Name = Circuit, Type = "Circuit", Description = "电源" },
        ]);
        _reader.Functions[Circuit] =
        [
            new FunctionRecord { Name = "=GAA+A1-FC1", VisibleName = "-FC1", IsMain = true, Category = "CircuitBreaker" },
            new FunctionRecord { Name = "=GAA+A1-FC1:1", VisibleName = "1", IsMain = false, Category = "NoContact" },
            new FunctionRecord { Name = "=GAA+A1-PF1", VisibleName = ">=>+>-PF1", IsMain = true, Category = "SignalLamp" },
            new FunctionRecord { Name = string.Empty, VisibleName = string.Empty, IsMain = true, Category = "Blackbox" },
            new FunctionRecord { Name = "=GAA+A1-TA1", VisibleName = string.Empty, IsMain = true, Category = "Transformer" },
        ];
    }

    [Fact]
    public void Execute_ExactName_ReturnsThePageAndItsNamedMainFunctionsInOrder()
    {
        PageGetData data = Execute(new PageGetArgs { Project = "Line2", Page = Circuit, Language = "zh_CN" }, _line1, _line2);

        Assert.Equal("Line2", data.Project);
        Assert.Equal((Circuit, "Circuit", "Circuit (UI)", "电源"), (data.Page.Name, data.Page.Type, data.Page.TypeName, data.Page.Description));
        Assert.Equal(
            [("=GAA+A1-FC1", "-FC1", "CircuitBreaker"), ("=GAA+A1-PF1", "=+-PF1", "SignalLamp"), ("=GAA+A1-TA1", null, "Transformer")],
            data.Devices.Select(d => (d.Tag, d.VisibleTag, d.Category)));
        Assert.Null(data.Properties);
        Assert.Equal([(_line2.Path, Circuit, false, "zh_CN")], _reader.ReadCalls);
    }

    [Fact]
    public void Execute_OtherCaseMatchingOnePage_IsRetriedWithTheExactName()
    {
        PageGetData data = Execute(new PageGetArgs { Page = "=gaa+a1&efs1/1" }, _line1);

        Assert.Equal(Circuit, data.Page.Name);
        Assert.Equal(["=gaa+a1&efs1/1", Circuit], _reader.ReadCalls.Select(c => c.PageName));
    }

    [Fact]
    public void Execute_OtherCaseMatchingSeveralPages_IsAmbiguousWithTheExactNames()
    {
        _reader.Pages.Add(new PageSummary { Name = "=gaa+A1&EFS1/1", Type = "Circuit" });

        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new PageGetArgs { Page = "=GAA+A1&efs1/1" }, _line1));

        Assert.Equal(ErrorReason.AmbiguousName, ex.Error.Reason);
        Assert.Equal("page", ex.Error.Metadata!["kind"]);
        Assert.Equal([Circuit, "=gaa+A1&EFS1/1"], ex.Error.Metadata["candidates"].Split('\n'));
    }

    [Fact]
    public void Execute_NoSuchPage_IsPageNotFoundPointingToPageList()
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new PageGetArgs { Page = "=GAA+A1&EFS1/9" }, _line1));

        Assert.Equal(ErrorReason.PageNotFound, ex.Error.Reason);
        Assert.False(ex.Error.Retryable);
        Assert.Contains("eplan_page_list with name_prefix", ex.Error.Message, StringComparison.Ordinal);
        Assert.Equal("=GAA+A1&EFS1/9", ex.Error.Metadata!["page"]);
        Assert.Equal("Line1", ex.Error.Metadata["project"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Execute_NoPageName_IsInvalidArgumentBeforeAnyRead(string? page)
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new PageGetArgs { Page = page! }, _line1));

        Assert.Equal(ErrorReason.InvalidArgument, ex.Error.Reason);
        Assert.Contains("'page'", ex.Error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_reader.ReadCalls);
    }

    [Fact]
    public void Execute_Detailed_ReturnsThePagePropertiesOrAnEmptyList()
    {
        _reader.Properties = [new PropertyEntry { Id = 11011, Name = "Page description", Value = "电源" }];

        PageGetData detailed = Execute(new PageGetArgs { Page = Circuit, IncludeProperties = true }, _line1);
        _reader.Properties = null;
        PageGetData none = Execute(new PageGetArgs { Page = Circuit, IncludeProperties = true }, _line1);

        Assert.Equal(11011, Assert.Single(detailed.Properties!).Id);
        Assert.True(_reader.ReadCalls[0].IncludeProperties);
        Assert.Empty(none.Properties!);
    }

    [Fact]
    public void Execute_PageWithoutFunctions_HasNoDevices()
    {
        Assert.Empty(Execute(new PageGetArgs { Page = "&AAA1/1" }, _line1).Devices);
    }

    [Fact]
    public void Execute_ProjectNotOpen_ReadsNoPage()
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new PageGetArgs { Project = "Line9", Page = Circuit }, _line1));

        Assert.Equal(ErrorReason.ProjectNotOpen, ex.Error.Reason);
        Assert.Empty(_reader.ReadCalls);
    }

    [Fact]
    public void Invoke_ThroughTheRouter_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        var router = new OperationRouter([new PageGetOperation(new FakeCatalog(_line1), _reader)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.PageGet, JObject.Parse("""{"page":"=GAA+A1&EFS1/1","language":"en_US","includeProperties":false}"""), "c");

        Assert.Equal("Line1", (string?)data["project"]);
        Assert.Equal("Circuit (UI)", (string?)data["page"]!["typeName"]);
        Assert.Equal("-FC1", (string?)data["devices"]![0]!["visibleTag"]);
        Assert.Equal(JTokenType.Null, data["properties"]!.Type);
    }

    private PageGetData Execute(PageGetArgs args, params OpenProject[] open) =>
        (PageGetData)new PageGetOperation(new FakeCatalog(open), _reader).Execute(args)!;
}
