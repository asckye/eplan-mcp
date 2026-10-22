using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// placeholder.list against a fake reader (docs/tools/eplan_placeholder_list.md, sections 3 and 5): the page filter,
/// the order by page and name with EPLAN's order kept for ties, paging, and the values only when detailed.
/// </summary>
[Trait("Size", "Small")]
public sealed class PlaceholderListOperationTests
{
    private const string Tm1 = "=TM1+A1&EFS1/1";
    private const string Tm2 = "=TM2+A1&EFS1/1";

    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Scratch\Line2" };

    private readonly FakePlaceholderReader _reader = new();
    private readonly FakePageReader _pages = new();

    public PlaceholderListOperationTests()
    {
        _pages.Pages.AddRange([new PageSummary { Name = Tm1, Type = "Circuit" }, new PageSummary { Name = Tm2, Type = "Circuit" }]);
        _reader.Placeholders.AddRange([FakePlaceholderReader.Sample(Tm2), FakePlaceholderReader.Sample(Tm1, "B"), FakePlaceholderReader.Sample(Tm1)]);
    }

    [Fact]
    public void Execute_TheWholeProject_SortsByPageThenName_AndGivesNoValuesWhenConcise()
    {
        PlaceholderListData data = Run(new PlaceholderListArgs { Language = "zh_CN", Limit = 20 });

        Assert.Equal(("Line1", null, 3), (data.Project, data.Page, data.Total));
        Assert.Equal([(Tm1, ""), (Tm1, "B"), (Tm2, "")], data.Placeholders.Select(p => (p.Page, p.Name)));
        PlaceholderItem first = data.Placeholders[0];
        Assert.Equal(["V", "mA"], first.ValueSets);
        Assert.Equal(["T1", "T2"], first.Variables);
        Assert.Equal(2, first.AssignedObjects);
        Assert.Null(first.Values);
        Assert.Null(first.CurrentValues);
        Assert.Equal((_line1.Path, "zh_CN"), Assert.Single(_reader.Reads));
    }

    [Fact]
    public void Execute_Detailed_GivesEveryValueByValueSetThenVariable_AndTheCurrentValues()
    {
        _reader.Placeholders[2].Values[0].Value.Language = "de_DE";

        PlaceholderItem item = Run(new PlaceholderListArgs { Detailed = true, Limit = 1 }).Placeholders.Single();

        Assert.Equal(
            [("V", "T1", "0-10 V", "de_DE"), ("V", "T2", "10 V", null), ("mA", "T1", "4-20 mA", null), ("mA", "T2", "20 mA", null)],
            item.Values!.Select(v => (v.ValueSet, v.Variable, v.Text, v.TextLanguage)));
        Assert.Equal(["0-10 V", "10 V"], item.CurrentValues!.Select(c => c.Text));
        Assert.All(item.CurrentValues!, c => Assert.Null(c.ValueSet));
    }

    [Fact]
    public void Execute_APage_KeepsOnlyItsPlaceholders_AsEplanNamesIt()
    {
        PlaceholderListData data = Run(new PlaceholderListArgs { Page = "=tm1+a1&efs1/1", Limit = 20 });

        Assert.Equal((Tm1, 2), (data.Page, data.Total));
        Assert.Equal(ErrorReason.PageNotFound, Fails(new PlaceholderListArgs { Page = "=TM9+A1&EFS1/1", Limit = 20 }).Reason);
    }

    [Fact]
    public void Execute_Slices_AndCountsEveryPlaceholder()
    {
        PlaceholderListData data = Run(new PlaceholderListArgs { Offset = 2, Limit = 2 });

        Assert.Equal([Tm2], data.Placeholders.Select(p => p.Page));
        Assert.Equal(3, data.Total);
    }

    [Fact]
    public void Execute_AProjectWithoutPlaceholders_IsAnEmptyList_AndAnUnknownProjectIsNotOpen()
    {
        _reader.Placeholders.Clear();

        Assert.Equal(0, Run(new PlaceholderListArgs { Project = "line2", Limit = 20 }).Total);
        Assert.Equal(_line2.Path, Assert.Single(_reader.Reads).Path);
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new PlaceholderListArgs { Project = "Line9", Limit = 20 }).Reason);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        var router = new OperationRouter([new PlaceholderListOperation(new FakeCatalog(_line1), _pages, _reader)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.PlaceholderList, JObject.Parse("""{"page":"=TM2+A1&EFS1/1","detailed":true,"offset":0,"limit":10}"""), "c");

        Assert.Equal((1, "mA"), ((int)data["total"]!, (string?)data["placeholders"]![0]!["valueSets"]![1]));
        Assert.Equal("4-20 mA", (string?)data["placeholders"]![0]!["values"]![2]!["text"]);
    }

    private PlaceholderListData Run(PlaceholderListArgs args) =>
        (PlaceholderListData)new PlaceholderListOperation(new FakeCatalog(_line1, _line2), _pages, _reader).Execute(args)!;

    private BridgeError Fails(PlaceholderListArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
}
