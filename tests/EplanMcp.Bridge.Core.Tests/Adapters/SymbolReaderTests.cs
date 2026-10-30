using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class SymbolReaderTests
{
    [Fact]
    public void Read_ResolvesEachPinCountOnceIncludingMissingDefinitions_AndRefreshesOnTheNextRead()
    {
        var project = new Project();
        SymbolVariant[] variants = [.. Enumerable.Range(0, 12).Select(i => new SymbolVariant
        {
            VariantNr = i,
            ConnectionPoints = [.. Enumerable.Range(0, i % 3 + 1).Select(_ => new NativeSymbolPoint())]
        })];
        project.SymbolLibraries = [new() { Symbols = [new() { Variants = variants }] }];
        project.FunctionDefinitionLibrary.Definitions = [new() { ConnectionPoints = [new()] }];
        EplanProjects.Project = project;
        var reader = new EplanSymbolReader();

        List<SymbolSearchItem> first = reader.Read("project", new() { Kind = "function" });

        Assert.Equal(12, first.Count);
        Assert.Equal(3, project.FunctionDefinitionLibrary.Reads);
        Assert.All(first, s => Assert.Equal(s.ConnectionPoints.Count == 1 ? "1/2" : null, s.PlcInputDefinition));
        Assert.All(first, s => Assert.All(s.ConnectionPoints, p =>
        {
            Assert.Equal([1d, 2d], p.Location);
            Assert.Equal("Up", p.Direction);
        }));

        project.FunctionDefinitionLibrary.Definitions = [new() { Id = 3, ConnectionPoints = [new(), new()] }];
        List<SymbolSearchItem> second = reader.Read("project", new() { Kind = "function" });

        Assert.Equal(6, project.FunctionDefinitionLibrary.Reads);
        Assert.All(second, s => Assert.Equal(s.ConnectionPoints.Count == 2 ? "1/3" : null, s.PlcInputDefinition));
        Assert.All(first, s => Assert.Equal(s.ConnectionPoints.Count == 1 ? "1/2" : null, s.PlcInputDefinition));
    }
}
