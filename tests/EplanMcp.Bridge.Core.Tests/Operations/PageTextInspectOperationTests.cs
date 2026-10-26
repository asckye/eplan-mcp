using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageTextInspectOperationTests
{
    [Fact]
    public void Inspect_ExplicitPages_ReturnsUnshapedEvidenceWithoutWriteSemantics()
    {
        var inspector = new Reader();
        var operation = new PageTextInspectOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = "project" }), inspector);

        var data = (List<PageTextInspection>)operation.Execute(new ProjectSnapshotArgs { Project = "Line1", Pages = ["=A/1"] })!;

        Assert.False((object)operation is IWriteOperation);
        Assert.Equal("project", inspector.Path);
        Assert.Equal("raw\u202E\n", Assert.Single(data).Texts[7]["??_??"]);
        Assert.Equal(3, data[0].FrameTextCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Inspect_OutsidePageBounds_RefusesBeforeAdapter(int count)
    {
        var reader = new Reader();
        var operation = new PageTextInspectOperation(new FakeCatalog(), reader);

        Assert.Throws<BridgeErrorException>(() => operation.Execute(new ProjectSnapshotArgs { Pages = [.. Enumerable.Repeat("=A/1", count)] }));
        Assert.Null(reader.Path);
    }

    private sealed class Reader : IPageTextInspector
    {
        public string? Path { get; private set; }

        public IReadOnlyList<PageTextInspection> Inspect(string projectPath, IReadOnlyList<string> pages)
        {
            Path = projectPath;
            return [new() { Page = pages[0], Texts = new() { [7] = new() { ["??_??"] = "raw\u202E\n" } }, FrameTextCount = 3, ProtectedHash = "hash" }];
        }
    }
}
