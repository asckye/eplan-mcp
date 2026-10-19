using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageListOperationTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };

    private readonly FakePageReader _reader = new();

    public PageListOperationTests()
    {
        _reader.Pages.AddRange(
        [
            new PageSummary { Name = "&AAA1/1", Type = "TitlePage", Description = "Title page / cover sheet" },
            new PageSummary { Name = "=GAA+A1&EFS1/1", Type = "Circuit", Description = "Power SUPPLY 24 V" },
            new PageSummary { Name = "=GAA+A1&EFS1/2", Type = "Circuit", Description = "电源 / power supply" },
            new PageSummary { Name = "=GAA+A1&EFS1/3", Type = "Circuit" },
            new PageSummary { Name = "=GAA+A2&EFS1/1", Type = "TerminalDiagram", Description = "Terminal strip -X1" },
        ]);
    }

    [Fact]
    public void Execute_ResolvesTheProjectAndPassesTheEplanFiltersToTheReader()
    {
        ListData<PageSummary> data = Execute(new PageListArgs { Project = "Line2", NamePrefix = "=GAA", PageType = "Circuit", Language = "zh_CN", Limit = 10 }, _line1, _line2);

        Assert.Equal("Line2", data.Project);
        Assert.Equal([(_line2.Path, "=GAA", "Circuit", "zh_CN")], _reader.ListCalls);
        Assert.Equal(["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2", "=GAA+A1&EFS1/3"], data.Items.Select(p => p.Name));
        Assert.Equal(3, data.Total);
    }

    [Theory]
    [InlineData("terminaldiagram")]
    [InlineData("TERMINALDIAGRAM")]
    [InlineData("TerminalDiagram")]
    public void Execute_PageTypeInAnyCase_IsPassedInEplansSpelling(string pageType)
    {
        ListData<PageSummary> data = Execute(new PageListArgs { PageType = pageType, Limit = 10 }, _line1);

        Assert.Equal("TerminalDiagram", _reader.ListCalls.Single().PageType);
        Assert.Equal("=GAA+A2&EFS1/1", Assert.Single(data.Items).Name);
    }

    [Theory]
    [InlineData("Schematic")]
    [InlineData("Circuits")]
    public void Execute_UnknownPageType_IsInvalidArgumentNamingCommonTypesBeforeAnyRead(string pageType)
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new PageListArgs { PageType = pageType, Limit = 10 }));

        Assert.Equal(ErrorReason.InvalidArgument, ex.Error.Reason);
        string violations = ex.Error.Metadata!["violations"];
        Assert.Contains("'page_type'", violations, StringComparison.Ordinal);
        Assert.Contains("Circuit, CircuitSingleLine", violations, StringComparison.Ordinal);
        Assert.Contains("eplan_tool_usage", violations, StringComparison.Ordinal);
        Assert.Empty(_reader.ListCalls);
    }

    [Fact]
    public void Execute_Text_MatchesDescriptionsIgnoringCaseInEplansOrder()
    {
        ListData<PageSummary> data = Execute(new PageListArgs { Text = "power supply", Limit = 10 }, _line1);

        Assert.Equal(["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"], data.Items.Select(p => p.Name));
        Assert.Equal(2, data.Total);
    }

    [Fact]
    public void Execute_Text_MatchesChineseAndSkipsPagesWithoutDescription()
    {
        ListData<PageSummary> data = Execute(new PageListArgs { Text = "电源", Limit = 10 }, _line1);

        Assert.Equal("=GAA+A1&EFS1/2", Assert.Single(data.Items).Name);
    }

    [Fact]
    public void Execute_FiltersCombineWithAnd()
    {
        ListData<PageSummary> data = Execute(new PageListArgs { NamePrefix = "=GAA+A1", PageType = "Circuit", Text = "24 V", Limit = 10 }, _line1);

        Assert.Equal("=GAA+A1&EFS1/1", Assert.Single(data.Items).Name);
    }

    [Fact]
    public void Execute_EmptyFilters_MeanNoFilter()
    {
        ListData<PageSummary> data = Execute(new PageListArgs { NamePrefix = string.Empty, PageType = string.Empty, Text = string.Empty, Limit = 10 }, _line1);

        Assert.Equal((null, null), (_reader.ListCalls.Single().NamePrefix, _reader.ListCalls.Single().PageType));
        Assert.Equal(5, data.Total);
    }

    [Fact]
    public void Execute_SlicesAfterFilteringAndCountsEveryMatch()
    {
        ListData<PageSummary> data = Execute(new PageListArgs { PageType = "Circuit", Offset = 1, Limit = 1 }, _line1);

        Assert.Equal("=GAA+A1&EFS1/2", Assert.Single(data.Items).Name);
        Assert.Equal(3, data.Total);
    }

    [Fact]
    public void Execute_OffsetPastTheMatches_IsAnOutOfDateCursor()
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new PageListArgs { PageType = "Circuit", Offset = 4, Limit = 10 }, _line1));

        Assert.Equal(ErrorReason.InvalidArgument, ex.Error.Reason);
        Assert.Contains("'cursor' is out of date", ex.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ProjectNotOpen_ReadsNoPages()
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new PageListArgs { Project = "Line9", Limit = 10 }, _line1));

        Assert.Equal(ErrorReason.ProjectNotOpen, ex.Error.Reason);
        Assert.Empty(_reader.ListCalls);
    }

    [Fact]
    public void Execute_SeveralOpenAndNoneSelected_IsAmbiguous()
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new PageListArgs { Limit = 10 }, _line2, new OpenProject { Name = "Line3", Path = "p3" }));

        Assert.Equal(ErrorReason.AmbiguousName, ex.Error.Reason);
    }

    [Fact]
    public void Invoke_ThroughTheRouter_ParsesCamelCaseArgumentsAndReturnsTheListShape()
    {
        var router = new OperationRouter([new PageListOperation(new FakeCatalog(_line1), _reader)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(
            OperationNames.PageList,
            JObject.Parse("""{"namePrefix":"=GAA+A1","pageType":"circuit","text":"supply","language":"en_US","offset":0,"limit":1}"""),
            "c");

        Assert.Equal([(_line1.Path, "=GAA+A1", "Circuit", "en_US")], _reader.ListCalls);
        Assert.Equal("Line1", (string?)data["project"]);
        Assert.Equal(2, (int)data["total"]!);
        Assert.Equal("=GAA+A1&EFS1/1", (string?)data["items"]![0]!["name"]);
        Assert.Equal("Circuit", (string?)data["items"]![0]!["type"]);
    }

    private ListData<PageSummary> Execute(PageListArgs args, params OpenProject[] open) =>
        (ListData<PageSummary>)new PageListOperation(new FakeCatalog(open), _reader).Execute(args)!;
}
