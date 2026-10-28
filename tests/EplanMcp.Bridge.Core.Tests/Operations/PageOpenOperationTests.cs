using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageOpenOperationTests
{
    [Theory]
    [InlineData("", "=A/1", "/project")]
    [InlineData("Line1", "", "/page")]
    [InlineData(null, "=A/1", "/project")]
    [InlineData("Line1", null, "/page")]
    public void BlankTarget_RejectsBeforeCatalogOrEditor(string? project, string? page, string path)
    {
        var operation = new PageOpenOperation(new UnusedCatalog(), null);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new PageOpenArgs { Project = project!, Page = page! }));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Contains(path, error.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("save", "/save")]
    [InlineData("Project", "/Project")]
    [InlineData("a~/b", "/a~0~1b")]
    public void UnknownMember_IsRejectedWithEscapedPointer(string member, string path)
    {
        var operation = new PageOpenOperation(new UnusedCatalog(), null);
        var args = new JObject { ["project"] = "Line1", ["page"] = "=A/1", [member] = true };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.ParseArgs(args));
        Assert.Contains(path, error.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Offline_RefusesWithoutReadingSelection()
    {
        var operation = new PageOpenOperation(new UnusedCatalog(), null);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new PageOpenArgs { Project = "Line1", Page = "=A/1" }));
        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal(OperationNames.PageOpen, error.Error.Metadata!["missing"]);
        Assert.Null(error.Error.Execution);
        Assert.False((IOperation)operation is IWriteOperation);
    }

    [Theory]
    [InlineData("{\"project\":1,\"page\":\"=A/1\"}", "/project")]
    [InlineData("{\"project\":\"Line1\",\"page\":false}", "/page")]
    [InlineData("{\"project\":null,\"page\":\"=A/1\"}", "/project")]
    public void NonStringTarget_IsNotCoerced(string json, string path)
    {
        var operation = new PageOpenOperation(new UnusedCatalog(), null);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.ParseArgs(JObject.Parse(json)));
        Assert.Contains(path, error.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitProject_IgnoresCurrentAndRepeatedCallsKeepExactTarget()
    {
        var editor = new Editor();
        var operation = new PageOpenOperation(new Catalog(), editor);
        for (int i = 0; i < 2; i++)
        {
            var result = Assert.IsType<PageOpenData>(operation.Execute(new PageOpenArgs { Project = "Line2", Page = "=A/1" }));
            Assert.Equal(("Line2", "=A/1", true), (result.Project, result.Page, result.EditorOpened));
        }
        Assert.Equal(Enumerable.Repeat((@"D:\Projects\Line2", "=A/1"), 2), editor.Calls);
    }

    [Theory]
    [InlineData("Line1", "=A/1")]
    [InlineData("Line2", "=a/1")]
    [InlineData("Line2", "=A/")]
    [InlineData("Line2", "=A/99")]
    public void PageOutsideExactProjectScope_IsNotFound(string project, string page)
    {
        var operation = new PageOpenOperation(new Catalog(), new Editor());
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new PageOpenArgs { Project = project, Page = page }));
        Assert.Equal(ErrorReason.PageNotFound, error.Error.Reason);
        Assert.Equal((project, page), (error.Error.Metadata!["project"], error.Error.Metadata["page"]));
        Assert.Null(error.Error.Execution);
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() =>
        [
            new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true },
            new() { Name = "Line2", Path = @"D:\Projects\Line2" },
        ];
    }

    private sealed class Editor : IPageEditor
    {
        public List<(string Path, string Page)> Calls { get; } = [];
        public string? Open(string projectPath, string pageName)
        {
            Calls.Add((projectPath, pageName));
            string[] names = projectPath == @"D:\Projects\Line2" ? ["=A/1", "=A/10"] : ["=A/10"];
            return names.FirstOrDefault(n => string.Equals(n, pageName, StringComparison.Ordinal));
        }
    }

    private sealed class UnusedCatalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => throw new InvalidOperationException("Catalog must not be read.");
    }
}
