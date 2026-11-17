using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageDrawingGetOperationTests
{
    [Fact]
    public void UnknownMember_IsRejectedWithEscapedJsonPointer()
    {
        var operation = new PageDrawingGetOperation(new Catalog(), new Reader());
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.ParseArgs(JObject.Parse("""{"page":"P","extra~/":true}""")));
        Assert.Contains("/extra~0~1", error.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void ExactRead_UsesExplicitResolvedProjectAndReturnsDetachedEvidence()
    {
        var reader = new Reader();
        var operation = new PageDrawingGetOperation(new Catalog(), reader);
        Assert.Equal(OperationNames.PageDrawingGet, operation.Name);
        var args = (PageGetArgs)operation.ParseArgs(JObject.Parse("""{"project":"Line1","page":"P"}"""));
        var data = (PageDrawingGeometry)operation.Execute(args)!;
        Assert.Equal(("X:/Line1", "P"), reader.Last);
        Assert.Equal("X:/Line1", data.Project);
        Assert.Same(reader.Geometry, data);
    }

    /// <summary>Live 2026-11-16: whole model view pages passed the 16 MiB reply limit; a limit returns a slice, parents first.</summary>
    [Fact]
    public void DrawingLimit_ReturnsASliceWithTheWholePageTotalAndFingerprint()
    {
        var reader = new Reader();
        DrawingPlacement Row(long oid, bool child) => new() { Locator = new() { Oid = oid, Digest = new string('a', 64) }, Parent = child ? new() { Oid = 1 } : null };
        reader.Geometry.Placements.AddRange([Row(10, child: true), Row(1, false), Row(2, false), Row(11, child: true)]);
        string whole = reader.Geometry.Fingerprint();
        var operation = new PageDrawingGetOperation(new Catalog(), reader);
        var args = (PageGetArgs)operation.ParseArgs(JObject.Parse("""{"project":"Line1","page":"P","drawingOffset":1,"drawingLimit":2}"""));

        var data = (PageDrawingGeometry)operation.Execute(args)!;

        Assert.Equal([2L, 10L], data.Placements.Select(p => p.Locator.Oid));
        Assert.Equal((4, whole), (data.PlacementTotal, data.PlacementsFingerprint));
        Assert.Throws<BridgeErrorException>(() => operation.Execute(new PageGetArgs { Project = "Line1", Page = "P", DrawingLimit = 201 }));
        Assert.Equal(4, reader.Geometry.Placements.Count);
    }

    /// <summary>The slice is a new result; every page field the reader gives must travel with it.</summary>
    [Fact]
    public void DrawingLimit_CopiesEveryPageField()
    {
        var reader = new Reader();
        reader.Geometry.PageSize = [630, 445.5];
        reader.Geometry.PlotFrameSize = [420, 297];
        reader.Geometry.DrawingArea = [6, 24, 624, 438];
        reader.Geometry.ColumnEdges = [6, 67.8, 624];
        var data = (PageDrawingGeometry)new PageDrawingGetOperation(new Catalog(), reader).Execute(new PageGetArgs { Project = "Line1", Page = "P", DrawingLimit = 1 })!;
        string[] sliceOnly = [nameof(PageDrawingGeometry.Placements), nameof(PageDrawingGeometry.PlacementTotal), nameof(PageDrawingGeometry.PlacementsFingerprint)];
        foreach (var property in typeof(PageDrawingGeometry).GetProperties().Where(p => !sliceOnly.Contains(p.Name)))
        { Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(property.GetValue(reader.Geometry)), Newtonsoft.Json.JsonConvert.SerializeObject(property.GetValue(data))); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingPage_FailsWithPointerBeforeReader(string? page)
    {
        var reader = new Reader();
        var operation = new PageDrawingGetOperation(new Catalog(), reader);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new PageGetArgs { Page = page! }));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Contains("/page", error.Error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Null(reader.Last);
    }

    [Fact]
    public void PageDisappearedOrOtherCase_IsPageNotFoundWithoutSubstitution()
    {
        var operation = new PageDrawingGetOperation(new Catalog(), new Reader());
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new PageGetArgs { Page = "p" }));
        Assert.Equal(ErrorReason.PageNotFound, error.Error.Reason);
        Assert.Contains("eplan_page_list", error.Error.Message, StringComparison.Ordinal);
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Line1", Path = "X:/Line1", Current = true }];
    }

    private sealed class Reader : IPageDrawingReader
    {
        public PageDrawingGeometry Geometry { get; } = new() { Page = "P", GridSize = 4 };
        public (string Project, string Page)? Last { get; private set; }
        public PageDrawingGeometry? Read(string projectPath, string pageName)
        {
            Last = (projectPath, pageName);
            return pageName == "P" ? Geometry : null;
        }
    }
}
