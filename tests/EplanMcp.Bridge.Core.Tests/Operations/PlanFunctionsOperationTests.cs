using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlanFunctionsOperationTests
{
    [Fact]
    public void Read_UnionKeepsUnplacedAndRepeatedMains_AndSortsWithoutGrouping()
    {
        var reader = new Reader
        {
            Functions =
            [
                Function(4, "+A1-K1", "+A1/2", true), Function(3, "+A1-K1", null),
                Function(2, "+A1-K1", "+A1/1", true), Function(5, "", "+A1/2"),
                Function(6, "+A10-K1", "+A10/1"), Function(1, "+A1-K1", "+A1/1", true),
            ],
        };
        PlanFunctionsData data = Run(reader, new() { Project = "Line1", Names = ["+A1-K1", "+A1-K1"], Pages = ["+A1/2"] });
        Assert.Equal([5L, 3, 1, 2, 4], data.Functions.Select(f => f.Oid));
        Assert.Equal(3, data.Functions.Count(f => f.IsMain));
        Assert.Equal(("Line1", Project, true, 10_000), (data.Project, data.ProjectPath, data.Complete, data.Limit));
        Assert.All(data.Functions, f => Assert.Equal("function", f.Kind));
        Assert.Equal(Project, reader.Path);
    }

    [Theory]
    [InlineData(51, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void Read_OnlyTheGuardLimitTruncates_WithExplicitCompleteness(int count, bool complete)
    {
        var reader = new Reader { Functions = [.. Enumerable.Range(1, count).Select(i => Function(i, "+A1-K1", "+A1/1"))] };
        PlanFunctionsData data = Run(reader, new() { Project = "Line1", Names = ["+A1-K1"] });
        Assert.Equal(Math.Min(count, 10_000), data.Functions.Count);
        Assert.Equal(complete, data.Complete);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"project":"Line1","names":[]}""")]
    [InlineData("""{"project":"Line1","names":[null]}""")]
    [InlineData("""{"project":"Line1","names":[" "]}""")]
    [InlineData("""{"project":"Line1","names":["+A1-K1"],"pages":[]}""")]
    [InlineData("""{"project":"Line1","names":["+A1-K1"],"pages":[null]}""")]
    public void Read_InvalidInput_NeverReads(string json)
    {
        var reader = new Reader();
        var operation = Operation(reader);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => operation.Execute(operation.ParseArgs(JToken.Parse(json)))).Error.Reason);
        Assert.Null(reader.Path);
    }

    [Theory]
    [InlineData(201, 1)]
    [InlineData(1, 51)]
    public void Read_TooManySelectors_NeverReads(int names, int pages)
    {
        var reader = new Reader();
        Assert.Throws<BridgeErrorException>(() => Run(reader, new() { Project = "Line1", Names = [.. Enumerable.Repeat("+A1-K1", names)], Pages = [.. Enumerable.Repeat("+A1/1", pages)] }));
        Assert.Null(reader.Path);
    }

    [Fact]
    public void Read_ExactNames_DoNotMatchCaseOrSuffixes()
    {
        var reader = new Reader { Functions = [Function(1, "+A1-K1:1", null), Function(2, "+a1-K1", null)] };
        Assert.Empty(Run(reader, new() { Project = "Line1", Names = ["+A1-K1"] }).Functions);
    }

    private const string Project = @"D:\Projects\Line1";
    private static PlanFunctionsOperation Operation(Reader reader) => new(new Catalog(), reader);
    private static PlanFunctionsData Run(Reader reader, PlanFunctionsArgs args) =>
        (PlanFunctionsData)Operation(reader).Execute(args)!;
    private static PlanFunction Function(long oid, string name, string? page, bool main = false) =>
        new() { Type = 17, Oid = oid, Name = name, Page = page, IsMain = main };

    private sealed class Reader : IPlanFunctionReader
    {
        public string? Path { get; private set; }
        public IReadOnlyList<PlanFunction> Functions { get; init; } = [];
        public IReadOnlyList<PlanFunction> Read(string projectPath) { Path = projectPath; return Functions; }
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Line1", Path = Project, ReadOnly = true }];
    }
}
