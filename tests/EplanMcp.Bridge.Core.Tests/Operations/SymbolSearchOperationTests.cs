using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class SymbolSearchOperationTests
{
    private readonly Reader _reader = new();
    private SymbolSearchOperation Operation => new(new Catalog(), _reader);

    [Theory]
    [InlineData(" PushButton ", 2)]
    [InlineData("", 3)]
    [InlineData(null, 3)]
    [InlineData("absent", 0)]
    public void TextFilter_MatchesNameOrDescriptionIgnoringCase_BeforePaging(string? text, int total)
    {
        _reader.Items.AddRange([
            Item("A", "Plain", 0),
            new() { Library = "A", Name = "A", Description = "NO PUSHBUTTON contact", Variant = 1 },
            Item("A", "pushbutton", 2),
        ]);
        var first = Run(new() { Project = "Line1", TextContains = text, Limit = 1 });
        Assert.Equal(total, first.Total);
        Assert.Equal(total == 0 ? 0 : 1, first.Items.Count);
        if (total == 2)
        {
            Assert.Equal(1, Assert.Single(first.Items).Variant);
            var next = Run(new() { Project = "Line1", TextContains = text, Limit = 1, Offset = 1 });
            Assert.Equal(2, next.Total);
            Assert.Equal(2, Assert.Single(next.Items).Variant);
        }
    }

    [Fact]
    public void Search_SortsVariantsAndPagesWithoutDroppingIdentity()
    {
        _reader.Items.AddRange([Item("Z", "K", 0), Item("A", "K", 2), Item("A", "K", 0), Item("A", "A", 1)]);
        var data = Run(new() { Project = "Line1", Offset = 1, Limit = 2 });
        Assert.Equal(("Line1", 4), (data.Project, data.Total));
        Assert.Equal([("A", "K", 0), ("A", "K", 2)], data.Items.Select(s => (s.Library, s.Name, s.Variant)));
        Assert.Equal("project-path", _reader.Path);
    }

    [Theory]
    [InlineData(null, null, 50, 0)]
    [InlineData(" ", null, 50, 0)]
    [InlineData("Line1", "Routing", 50, 0)]
    [InlineData("Line1", "", 50, 0)]
    [InlineData("Line1", null, 0, 0)]
    [InlineData("Line1", null, 201, 0)]
    [InlineData("Line1", null, 50, -1)]
    public void InvalidArguments_ReadNothing(string? project, string? kind, int limit, int offset)
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(new() { Project = project, Kind = kind, Limit = limit, Offset = offset })).Error.Reason);
        Assert.Null(_reader.Path);
    }

    [Fact]
    public void MissingProjectAndOutdatedOffset_AreErrors()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Assert.Throws<BridgeErrorException>(() => Run(new() { Project = "missing", Limit = 50 })).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(new() { Project = "Line1", Limit = 50, Offset = 1 })).Error.Reason);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteGeometry_IsNotSerialized(double value)
    {
        SymbolSearchItem item = Item("A", "K", 0);
        item.ConnectionPoints.Add(new() { Location = [0, value] });
        _reader.Items.Add(item);
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => Run(new() { Project = "Line1", Limit = 50 })).Error.Reason);
    }

    [Fact]
    public void Parser_RejectsUnknownMembers()
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Operation.ParseArgs(JObject.Parse("""{"project":"Line1","initialize":true}"""))).Error.Reason);
        var args = Assert.IsType<SymbolSearchArgs>(Operation.ParseArgs(JObject.Parse("""{"project":"Line1","limit":50,"textContains":"pushbutton","language":"en_US"}""")));
        Assert.Equal(("pushbutton", "en_US"), (args.TextContains, args.Language));
    }

    private ListData<SymbolSearchItem> Run(SymbolSearchArgs args) => (ListData<SymbolSearchItem>)Operation.Execute(args)!;
    private static SymbolSearchItem Item(string library, string name, int variant) => new() { Library = library, Name = name, Variant = variant };
    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Line1", Path = "project-path" }];
    }
    private sealed class Reader : ISymbolReader
    {
        public List<SymbolSearchItem> Items { get; } = [];
        public string? Path { get; private set; }
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) { Path = projectPath; return Items; }
    }
}
