using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ProjectGetOperationTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2", ReadOnly = true };

    private readonly FakeProjectReader _reader = new()
    {
        Content = new ProjectContent
        {
            PageTypes = ["TitlePage", "Circuit", "TerminalDiagram", "Circuit", "Overview", "Circuit", "TerminalDiagram"],
            Devices = 420,
            Connections = 2060,
            PartReferences = 2900,
            Properties = [new PropertyEntry { Id = 10000, Value = "Line1" }],
        },
    };

    [Fact]
    public void Execute_ResolvesTheNamedProjectAndReadsItByPath()
    {
        ProjectGetData data = Execute(new ProjectGetArgs { Project = "line2", Language = "en_US" }, _line1, _line2);

        Assert.Same(_line2, data.Project);
        Assert.Equal([(_line2.Path, false, "en_US")], _reader.Calls);
    }

    [Fact]
    public void Execute_CountsPagesAndPassesTheReadersCountsThrough()
    {
        ProjectGetData data = Execute(new ProjectGetArgs(), _line1, _line2);

        Assert.Same(_line1, data.Project);
        Assert.Equal((7, 420, 2060, 2900), (data.Pages, data.Devices, data.Connections, data.PartReferences));
    }

    [Fact]
    public void Execute_GroupsPagesByTypeMostFrequentFirstAndEqualCountsByName()
    {
        ProjectGetData data = Execute(new ProjectGetArgs(), _line1);

        Assert.Equal(
            [("Circuit", 3), ("TerminalDiagram", 2), ("Overview", 1), ("TitlePage", 1)],
            data.PagesByType.Select(t => (t.Type, t.Count)));
    }

    [Fact]
    public void Execute_PropertiesOnlyWhenAskedFor()
    {
        Assert.Null(Execute(new ProjectGetArgs(), _line1).Properties);

        ProjectGetData detailed = Execute(new ProjectGetArgs { IncludeProperties = true }, _line1);

        Assert.Equal("Line1", Assert.Single(detailed.Properties!).Value);
        Assert.True(_reader.Calls[^1].IncludeProperties);
    }

    [Fact]
    public void Execute_PropertiesAskedForButNoneRead_IsAnEmptyList()
    {
        _reader.Content.Properties = null;

        Assert.Empty(Execute(new ProjectGetArgs { IncludeProperties = true }, _line1).Properties!);
    }

    [Fact]
    public void Execute_NoProjectOpen_IsProjectNotOpenWithoutReading()
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new ProjectGetArgs()));

        Assert.Equal(ErrorReason.ProjectNotOpen, ex.Error.Reason);
        Assert.Empty(_reader.Calls);
    }

    [Fact]
    public void Execute_TwoProjectsShareTheName_IsAmbiguousWithBothPaths()
    {
        var copy = new OpenProject { Name = "Line1", Path = @"E:\Archive\Line1" };

        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(new ProjectGetArgs { Project = "Line1" }, _line1, copy));

        Assert.Equal(ErrorReason.AmbiguousName, ex.Error.Reason);
        Assert.Equal([_line1.Path, copy.Path], ex.Error.Metadata!["candidates"].Split('\n'));
        Assert.Empty(_reader.Calls);
    }

    [Fact]
    public void Invoke_ThroughTheRouter_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        var router = new OperationRouter([new ProjectGetOperation(new FakeCatalog(_line1), _reader)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.ProjectGet, JObject.Parse("""{"project":"Line1","language":"zh_CN","includeProperties":true}"""), "c");

        Assert.Equal([(_line1.Path, true, "zh_CN")], _reader.Calls);
        Assert.Equal(2900, (int)data["partReferences"]!);
        Assert.Equal("Circuit", (string?)data["pagesByType"]![0]!["type"]);
        Assert.Equal(10000, (int)data["properties"]![0]!["id"]!);
        Assert.Equal(JTokenType.Null, data["properties"]![0]!["index"]!.Type);
    }

    private ProjectGetData Execute(ProjectGetArgs args, params OpenProject[] open) =>
        (ProjectGetData)new ProjectGetOperation(new FakeCatalog(open), _reader).Execute(args)!;
}
