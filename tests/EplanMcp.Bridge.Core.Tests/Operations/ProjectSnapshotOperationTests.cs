using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// project.snapshot over a fake reader (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.1, 7): pages in order of their
/// numbers, chunks that stop at the time limit and continue, page selection, and the four checks before a project that is
/// not open is opened read-only, against project files in a temporary directory.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectSnapshotOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-snapshot-").FullName;
    private readonly FakeSnapshotReader _reader = new();
    private readonly ProjectSnapshotOperation _operation;

    public ProjectSnapshotOperationTests() =>
        _operation = new ProjectSnapshotOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = @"C:\scratch\Line1", Current = true }), _reader, _reader, () => _reader.Milliseconds);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Snapshot_OfTheWholeProjectInOneChunk_HasPagesInOrderOfTheirNumbers_AndTheSnapshotHash()
    {
        ProjectSnapshotData data = Run(new ProjectSnapshotArgs { TimeLimitSeconds = 20 });

        Assert.Equal(("Line1", @"C:\scratch\Line1", "Line1@1792643412", "1792643412 36000"), (data.Project, data.ProjectPath, data.ProjectGuid, data.Modified));
        Assert.Equal(("standard", "eplanmcp.snapshot/1", 5, true, (long?)null), (data.Profile, data.Format, data.PageCount, data.Complete, data.NextAfterPage));
        Assert.Equal([100L, 110, 120, 130, 140], data.Pages.Select(p => p.Oid));
        Assert.Equal(SnapshotCanonical.SnapshotHash("standard", data.Pages), data.SnapshotHash);
        Assert.Equal(["page", "function", "connection"], data.Pages[0].Objects.Select(o => o.Kind));
        Assert.Equal((5, 5000L, 1), (data.SkippedConnections, data.Milliseconds, _reader.Disposed));
    }

    [Fact]
    public void Snapshot_AtTheTimeLimit_StopsBetweenPages_AndTheNextChunkContinues_ToTheSameHash()
    {
        ProjectSnapshotData whole = Run(new ProjectSnapshotArgs { TimeLimitSeconds = 20 });

        ProjectSnapshotData first = Run(new ProjectSnapshotArgs { TimeLimitSeconds = 2 });
        ProjectSnapshotData second = Run(new ProjectSnapshotArgs { TimeLimitSeconds = 2, AfterPage = first.NextAfterPage });
        ProjectSnapshotData third = Run(new ProjectSnapshotArgs { TimeLimitSeconds = 2, AfterPage = second.NextAfterPage });

        Assert.Equal([100L, 110], first.Pages.Select(p => p.Oid));
        Assert.Equal((false, (long?)110, (string?)null), (first.Complete, first.NextAfterPage, first.SnapshotHash));
        Assert.Equal([120L, 130], second.Pages.Select(p => p.Oid));
        Assert.Equal([140L], third.Pages.Select(p => p.Oid));
        Assert.Equal((true, (string?)null), (third.Complete, third.SnapshotHash));
        Assert.Equal(whole.SnapshotHash, SnapshotCanonical.SnapshotHash("standard", [.. first.Pages, .. second.Pages, .. third.Pages]));
    }

    [Fact]
    public void Snapshot_PastTheTimeLimitAlready_StillTakesOnePage()
    {
        _reader.MillisecondsPerPage = 60_000;

        ProjectSnapshotData data = Run(new ProjectSnapshotArgs { TimeLimitSeconds = 1 });

        Assert.Equal([100L], data.Pages.Select(p => p.Oid));
        Assert.Equal(100, data.NextAfterPage);
    }

    [Fact]
    public void Snapshot_OfNamedPages_TakesThoseThatExist_AndListsTheOthers_WithoutASnapshotHash()
    {
        ProjectSnapshotData data = Run(new ProjectSnapshotArgs { Pages = ["=AP+ST1/13", " =AP+ST1/10 ", "=AP+ST1/99", "=ap+st1/11"] });

        Assert.Equal(["=AP+ST1/10", "=AP+ST1/13"], data.Pages.Select(p => p.Name));
        Assert.Equal(["=AP+ST1/99", "=ap+st1/11"], data.MissingPages);
        Assert.Equal((2, true, (string?)null), (data.PageCount, data.Complete, data.SnapshotHash));
    }

    [Fact]
    public void Snapshot_AfterReopening_HasTheSameHashes()
    {
        string before = Run(new ProjectSnapshotArgs()).SnapshotHash!;
        _reader.Database = 89; // A36: the database number changes on every opening

        Assert.Equal(before, Run(new ProjectSnapshotArgs()).SnapshotHash);
    }

    [Fact]
    public void Snapshot_OneChangedDescription_ChangesOnlyThatPagesHash()
    {
        ProjectSnapshotData before = Run(new ProjectSnapshotArgs());
        _reader.Descriptions[120] = "Neu";
        ProjectSnapshotData after = Run(new ProjectSnapshotArgs());

        Assert.Equal([120L], before.Pages.Zip(after.Pages).Where(p => p.First.Hash != p.Second.Hash).Select(p => p.First.Oid));
        Assert.NotEqual(before.SnapshotHash, after.SnapshotHash);
    }

    [Theory]
    [InlineData("""{"profile": "full"}""", "'profile'")]
    [InlineData("""{"timeLimitSeconds": 0}""", "'timeLimitSeconds'")]
    [InlineData("""{"timeLimitSeconds": 21}""", "'timeLimitSeconds'")]
    [InlineData("""{"pages": []}""", "'pages'")]
    public void Snapshot_InvalidArguments_AreInvalidArgument_AndOpenNothing(string json, string named)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => _operation.Execute(_operation.ParseArgs(JToken.Parse(json))));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Contains(named, error.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_reader.Opened);
    }

    [Fact]
    public void OpenReadOnly_OfAnOpenProject_ReadsTheOpenOne_AndOpensNothing()
    {
        Run(new ProjectSnapshotArgs { Project = @"C:\scratch\Line1.elk", OpenReadOnly = true });

        Assert.Equal([@"C:\scratch\Line1"], _reader.Opened);
        Assert.Empty(_reader.OpenedReadOnly);
    }

    [Fact]
    public void OpenReadOnly_OfACompleteCompatibleCopy_OpensItForTheCallAndClosesIt()
    {
        string link = Copy("Copy", "User.eod", "User.eox");

        ProjectSnapshotData data = Run(new ProjectSnapshotArgs { Project = link, OpenReadOnly = true });

        Assert.Equal([link], _reader.OpenedReadOnly);
        Assert.Equal((link[..^4], 1), (data.ProjectPath, _reader.Disposed));
    }

    [Fact]
    public void OpenReadOnly_ACopyWithoutEplansSessionFiles_IsNotOpened()
    {
        string link = Copy("Copy", "User.eod");

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Run(new ProjectSnapshotArgs { Project = link, OpenReadOnly = true }));

        Assert.Equal((ErrorReason.InvalidArgument, "User.eox"), (error.Error.Reason, error.Error.Metadata!["missing"]));
        Assert.Empty(_reader.OpenedReadOnly);
    }

    [Fact]
    public void OpenReadOnly_AProjectOfAnotherVersion_IsNotOpened()
    {
        string link = Copy("Old", "User.eod", "User.eox");
        _reader.Status = "NeedsUpgrade";

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Run(new ProjectSnapshotArgs { Project = link, OpenReadOnly = true }));

        Assert.Equal((ErrorReason.InvalidArgument, "NeedsUpgrade"), (error.Error.Reason, error.Error.Metadata!["status"]));
        Assert.Empty(_reader.OpenedReadOnly);
    }

    [Theory]
    [InlineData(@"relative\Copy.elk", ErrorReason.InvalidArgument)]
    [InlineData(@"\\server\share\Copy.elk", ErrorReason.InvalidArgument)]
    [InlineData(@"C:\scratch\..\Copy.elk", ErrorReason.InvalidArgument)]
    [InlineData(@"C:\scratch\Copy.edb", ErrorReason.InvalidArgument)]
    [InlineData(@"C:\nowhere-eplanmcp\Copy.elk", ErrorReason.ProjectNotOpen)]
    public void OpenReadOnly_APathThatIsNoProjectFile_IsRefused(string project, string reason)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Run(new ProjectSnapshotArgs { Project = project, OpenReadOnly = true }));

        Assert.Equal(reason, error.Error.Reason);
        Assert.Empty(_reader.OpenedReadOnly);
    }

    [Fact]
    public void WithoutOpenReadOnly_AProjectThatIsNotOpen_IsProjectNotOpen()
    {
        string link = Copy("Copy", "User.eod", "User.eox");

        Assert.Equal(ErrorReason.ProjectNotOpen, Assert.Throws<BridgeErrorException>(() => Run(new ProjectSnapshotArgs { Project = link })).Error.Reason);
        Assert.Empty(_reader.OpenedReadOnly);
    }

    private ProjectSnapshotData Run(ProjectSnapshotArgs args) =>
        (ProjectSnapshotData)_operation.Execute(_operation.ParseArgs(JObject.FromObject(args)))!;

    /// <summary>A project's link file and data folder holding <paramref name="files"/>; returns the link file.</summary>
    private string Copy(string name, params string[] files)
    {
        string link = Path.Combine(_root, name + ".elk");
        File.WriteAllText(link, string.Empty);
        string data = Directory.CreateDirectory(Path.Combine(_root, name + ".edb")).FullName;
        foreach (string file in files)
        {
            File.WriteAllText(Path.Combine(data, file), string.Empty);
        }

        return link;
    }
}
