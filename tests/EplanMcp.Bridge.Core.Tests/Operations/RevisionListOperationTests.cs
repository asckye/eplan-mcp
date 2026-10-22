using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// revision.list against a fake reader (docs/tools/eplan_revision_list.md, sections 3 and 5): the state by the link
/// file's extension and the log with Unix seconds as S23 r3 found them (api-notes A28).
/// </summary>
[Trait("Size", "Small")]
public sealed class RevisionListOperationTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Scratch\Line2" };

    private readonly FakeRevisionReader _reader = new();

    [Theory]
    [InlineData(@"D:\Scratch\Line1.elk", "none")]
    [InlineData(@"D:\Scratch\Line1.ell", "tracking")]
    [InlineData(@"D:\Scratch\Line1.ELR", "completed")]
    [InlineData(@"D:\Scratch\Line1.zw1", "unknown")]
    [InlineData(@"D:\Scratch.v2\Line1", "unknown")]
    [InlineData("", "unknown")]
    public void StateOf_IsTheLinkFilesExtension(string linkFile, string state)
    {
        Assert.Equal(state, RevisionListOperation.StateOf(linkFile));
    }

    [Theory]
    [InlineData("1792570500", "2026-10-21T08:15:00Z")]
    [InlineData(" 0 ", "1970-01-01T00:00:00Z")]
    [InlineData("-1", "1969-12-31T23:59:59Z")]
    [InlineData("21.10.2026", null)]
    [InlineData("99999999999999999", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void UtcDate_ReadsUnixSeconds(string? raw, string? date)
    {
        Assert.Equal(date, RevisionListOperation.UtcDate(raw));
    }

    [Fact]
    public void Execute_ATrackedRevision_GivesTheStateTheLogAndThePages()
    {
        _reader.Content = new RevisionContent
        {
            LinkFile = @"D:\Scratch\Line1.ell",
            TrackingStarted = true,
            Parent = @"D:\Scratch\Line0.elk",
            Log =
            [
                new RevisionRecord { Index = 2, Name = "S23-4", Date = "not a date" },
                new RevisionRecord { Index = 1, Name = "S23-3", Description = "spike", DescriptionLanguage = "de_DE", User = "ASCKYE", Date = "1792570500" },
            ],
            UncompletedPages = [.. Enumerable.Range(1, 60).Select(n => "=A/" + n)],
            MarkedPages = 3,
        };

        RevisionListData data = Run(new RevisionListArgs { Language = "zh_CN", Limit = 50 });

        Assert.Equal(("Line1", @"D:\Scratch\Line1.ell", "tracking", true, null), (data.Project, data.Path, data.State, data.TrackingStarted, data.Parent));
        Assert.Equal([1, 2], data.Revisions.Select(r => r.Index));
        RevisionItem first = data.Revisions[0];
        Assert.Equal(("S23-3", "spike", "de_DE", "ASCKYE", "2026-10-21T08:15:00Z", null), (first.Name, first.Description, first.DescriptionLanguage, first.User, first.Date, first.DateRaw));
        Assert.Equal((null, "not a date"), (data.Revisions[1].Date, data.Revisions[1].DateRaw));
        Assert.Equal(2, data.Total);
        Assert.Equal(50, data.UncompletedPages.Count);
        Assert.Equal(("=A/1", 60, 3), (data.UncompletedPages[0], data.UncompletedPageCount, data.MarkedPageCount));
        Assert.Equal((_line1.Path, "zh_CN"), Assert.Single(_reader.Reads));
    }

    [Fact]
    public void Execute_ACompletedRevision_NamesTheProjectItCameFrom()
    {
        _reader.Content = new RevisionContent { LinkFile = @"D:\Scratch\Line1.elr", Parent = @"D:\Scratch\Line1.ell" };

        RevisionListData data = Run(new RevisionListArgs { Limit = 50 });

        Assert.Equal(("completed", @"D:\Scratch\Line1.ell"), (data.State, data.Parent));
    }

    [Fact]
    public void Execute_AProjectWithoutRevisions_HasAnEmptyLog()
    {
        RevisionListData data = Run(new RevisionListArgs { Limit = 50 });

        Assert.Equal(("none", false, 0, 0), (data.State, data.TrackingStarted, data.Total, data.UncompletedPageCount));
        Assert.Empty(data.Revisions);
    }

    [Fact]
    public void Execute_Slices_AndCountsEveryRecord()
    {
        _reader.Content.Log = [.. Enumerable.Range(1, 5).Select(i => new RevisionRecord { Index = i, Name = "R" + i })];

        RevisionListData data = Run(new RevisionListArgs { Offset = 2, Limit = 2 });

        Assert.Equal(["R3", "R4"], data.Revisions.Select(r => r.Name));
        Assert.Equal(5, data.Total);
        Assert.Equal(ErrorReason.InvalidArgument, Fails(new RevisionListArgs { Offset = 6, Limit = 2 }).Reason);
    }

    [Fact]
    public void Execute_NamedProject_ReadsThatProjectsPathAndAnUnknownOneIsNotOpen()
    {
        Run(new RevisionListArgs { Project = "line2", Limit = 50 });

        Assert.Equal(_line2.Path, Assert.Single(_reader.Reads).Path);
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new RevisionListArgs { Project = "Line9", Limit = 50 }).Reason);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        _reader.Content.Log = [new RevisionRecord { Index = 1, Name = "S23-3", Date = "1792570500" }];
        var router = new OperationRouter([new RevisionListOperation(new FakeCatalog(_line1), _reader)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.RevisionList, JObject.Parse("""{"language":"en_US","offset":0,"limit":10}"""), "c");

        Assert.Equal(("none", 1, "2026-10-21T08:15:00Z"), ((string?)data["state"], (int)data["total"]!, (string?)data["revisions"]![0]!["date"]));
        Assert.Equal("en_US", Assert.Single(_reader.Reads).Language);
    }

    private RevisionListData Run(RevisionListArgs args) =>
        (RevisionListData)new RevisionListOperation(new FakeCatalog(_line1, _line2), _reader).Execute(args)!;

    private BridgeError Fails(RevisionListArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;

    /// <summary>Returns <see cref="Content"/> and records what the operation asked for.</summary>
    private sealed class FakeRevisionReader : IRevisionReader
    {
        public RevisionContent Content { get; set; } = new() { LinkFile = @"D:\Scratch\Line1.elk" };

        public List<(string Path, string? Language)> Reads { get; } = [];

        public RevisionContent Read(string projectPath, string? language)
        {
            Reads.Add((projectPath, language));
            return Content;
        }
    }
}
