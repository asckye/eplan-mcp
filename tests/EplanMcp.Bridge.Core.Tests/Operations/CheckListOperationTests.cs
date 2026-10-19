using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class CheckListOperationTests
{
    private const string Path1 = @"D:\Projects\Line1";
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = Path1, Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };

    private readonly FakeProjectMessageReader _reader = new();

    [Fact]
    public void Execute_ByDefault_ListsOpenMessagesInEplansOrderWithCounts()
    {
        _reader.Messages.AddRange(
        [
            Record(4025, "Warning", "Connections", "w1"),
            Record(13001, "Hint", "PartsData", "h1"),
            Record(4026, "Warning", "Connections", "done warning", done: true),
            Record(7001, "Error", "Devices", "e1"),
            Record(1, "Unknown", "Others", "u1"),
        ]);

        CheckListData data = Run(new CheckListArgs { Limit = 50 });

        Assert.Equal("Line1", data.Project);
        Assert.Equal(["w1", "h1", "e1", "u1"], data.Items.Select(m => m.Text));
        Assert.Equal(["warning", "hint", "error", "unknown"], data.Items.Select(m => m.Severity));
        Assert.Equal((4, 1, 1, 1, 1), (data.Total, data.Errors, data.Warnings, data.Hints, data.Done));
        Assert.Equal([Path1], _reader.Reads);
    }

    [Fact]
    public void Execute_IncludeDone_ListsAndCountsDoneMessages()
    {
        _reader.Messages.AddRange([Record(1, "Warning", "Cables", "open"), Record(2, "Warning", "Cables", "done", done: true)]);

        CheckListData data = Run(new CheckListArgs { IncludeDone = true, Limit = 50 });

        Assert.Equal([false, true], data.Items.Select(m => m.Done));
        Assert.Equal((2, 2, 1), (data.Total, data.Warnings, data.Done));
    }

    [Theory]
    [InlineData("warning", new[] { "w1", "w2" })]
    [InlineData(" ERROR ", new[] { "e1" })]
    [InlineData("hint", new string[0])]
    public void Execute_Severity_FiltersTheListButNotTheCounts(string severity, string[] expected)
    {
        _reader.Messages.AddRange([Record(1, "Warning", "Cables", "w1"), Record(2, "Error", "Devices", "e1"), Record(3, "Warning", "Plugs", "w2")]);

        CheckListData data = Run(new CheckListArgs { Severity = severity, Limit = 50 });

        Assert.Equal(expected, data.Items.Select(m => m.Text));
        Assert.Equal(expected.Length, data.Total);
        Assert.Equal((1, 2, 0), (data.Errors, data.Warnings, data.Hints));
    }

    [Fact]
    public void Execute_BlankSeverity_IsNoFilter()
    {
        _reader.Messages.AddRange([Record(1, "Warning", "Cables", "w"), Record(2, "Hint", "Cables", "h")]);

        Assert.Equal(2, Run(new CheckListArgs { Severity = " ", Limit = 50 }).Total);
    }

    [Fact]
    public void Execute_UnknownSeverity_IsInvalidArgumentBeforeAnythingIsRead()
    {
        BridgeError error = Fails(new CheckListArgs { Severity = "fatal", Limit = 50 });

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'severity'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_reader.Reads);
    }

    [Fact]
    public void Execute_Object_IsCarriedWithItsKindAndNameOrLeftNull()
    {
        _reader.Messages.AddRange(
        [
            Record(1, "Warning", "Connections", "a", kind: "Function", name: "=GAA+A1-FC1"),
            Record(2, "Warning", "Connections", "b", kind: "Connection"),
            Record(3, "Warning", "Connections", "c"),
        ]);

        CheckListData data = Run(new CheckListArgs { Limit = 50 });

        Assert.Equal(("Function", "=GAA+A1-FC1"), (data.Items[0].Target!.Kind, data.Items[0].Target!.Name));
        Assert.Equal(("Connection", (string?)null), (data.Items[1].Target!.Kind, data.Items[1].Target!.Name));
        Assert.Null(data.Items[2].Target);
        Assert.Equal(("Connections", 1), (data.Items[0].Region, data.Items[0].Number));
    }

    [Fact]
    public void Execute_NoMessages_IsAnEmptyListWithZeroCounts()
    {
        CheckListData data = Run(new CheckListArgs { Limit = 50 });

        Assert.Empty(data.Items);
        Assert.Equal((0, 0, 0, 0, 0), (data.Total, data.Errors, data.Warnings, data.Hints, data.Done));
    }

    [Fact]
    public void Execute_Slices_AndCountsEveryMatch()
    {
        _reader.Messages.AddRange(Enumerable.Range(1, 7).Select(i => Record(i, "Hint", "Others", "h" + i)));

        CheckListData data = Run(new CheckListArgs { Offset = 2, Limit = 3 });

        Assert.Equal(["h3", "h4", "h5"], data.Items.Select(m => m.Text));
        Assert.Equal((7, 7), (data.Total, data.Hints));
    }

    [Fact]
    public void Execute_OffsetPastTheMatches_IsInvalidArgument()
    {
        _reader.Messages.Add(Record(1, "Hint", "Others", "h"));

        Assert.Equal(ErrorReason.InvalidArgument, Fails(new CheckListArgs { Offset = 5, Limit = 5 }).Reason);
    }

    [Fact]
    public void Execute_NamedProject_ReadsThatProjectsPath()
    {
        Run(new CheckListArgs { Project = "line2", Limit = 50 });

        Assert.Equal([_line2.Path], _reader.Reads);
    }

    [Fact]
    public void Execute_UnknownProject_IsProjectNotOpen()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new CheckListArgs { Project = "Line9", Limit = 50 }).Reason);
        Assert.Empty(_reader.Reads);
    }

    [Theory]
    [InlineData("Error", "error")]
    [InlineData("Warning", "warning")]
    [InlineData("Hint", "hint")]
    [InlineData("Unknown", "unknown")]
    [InlineData("7", "unknown")]
    public void Severity_MapsEplansClassification(string category, string expected)
    {
        Assert.Equal(expected, CheckListOperation.Severity(category));
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        _reader.Messages.AddRange([Record(4025, "Warning", "Connections", "端子 -X1 没有连接", kind: "Terminal", name: "=GAA+A1-X1:1"), Record(2, "Hint", "Others", "h", done: true)]);
        var router = new OperationRouter([new CheckListOperation(new FakeCatalog(_line1), _reader)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.CheckList, JObject.Parse("""{"severity":"warning","includeDone":true,"offset":0,"limit":10}"""), "c");

        Assert.Equal((1, 1, 1), ((int)data["total"]!, (int)data["warnings"]!, (int)data["hints"]!));
        Assert.Equal("=GAA+A1-X1:1", (string?)data["items"]![0]!["target"]!["name"]);
        Assert.Equal("warning", (string?)data["items"]![0]!["severity"]);
    }

    private static ProjectMessageRecord Record(int id, string category, string region, string text, bool done = false, string? kind = null, string? name = null) =>
        new() { Id = id, Category = category, Region = region, Text = text, Done = done, ObjectKind = kind, ObjectName = name };

    private CheckListData Run(CheckListArgs args) =>
        (CheckListData)new CheckListOperation(new FakeCatalog(_line1, _line2), _reader).Execute(args)!;

    private BridgeError Fails(CheckListArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;

    /// <summary>One project's check messages in EPLAN's order; records the path of every read.</summary>
    private sealed class FakeProjectMessageReader : IProjectMessageReader
    {
        public List<ProjectMessageRecord> Messages { get; } = [];

        public List<string> Reads { get; } = [];

        public IReadOnlyList<ProjectMessageRecord> ReadMessages(string projectPath)
        {
            Reads.Add(projectPath);
            return [.. Messages];
        }
    }
}
