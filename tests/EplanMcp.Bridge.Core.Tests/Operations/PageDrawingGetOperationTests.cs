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
