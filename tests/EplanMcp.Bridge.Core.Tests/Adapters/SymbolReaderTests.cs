using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class SymbolReaderTests
{
    [Theory]
    [InlineData("en_US", "zh_CN;", "de_DE@First;en_US@Requested;zh_CN@Display;", "Requested")]
    [InlineData(null, "zh_CN;en_US;", "de_DE@First;en_US@English;zh_CN@Display;", "Display")]
    [InlineData("zh_CN", "en_US;", "de_DE@First;en_US@Display;", "Display")]
    [InlineData(null, "zh_CN;", "de_DE@First;en_US@English;", "First")]
    [InlineData("zh_CN", "zh_CN;", "de_DE@;en_US@First non-empty;", "First non-empty")]
    [InlineData(null, "##_##;", "de_DE@First;en_US@English;", "First")]
    [InlineData(null, "", "de_DE@First;en_US@English;", "First")]
    [InlineData("??_??", "zh_CN;", "??_??@Independent;", "Independent")]
    [InlineData(null, "zh_CN;", null, null)]
    [InlineData(null, "zh_CN;", "", null)]
    [InlineData(null, "zh_CN;", "de_DE@;en_US@;", null)]
    public void Description_UsesRequestedProjectThenStoredLanguage_OrNull(string? language, string display, string? raw, string? expected)
    {
        var symbol = new Symbol { Variants = [new(), new() { VariantNr = 1 }] };
        symbol.StoredProperties.Description = raw;
        EplanProjects.Project = new() { Display = display, SymbolLibraries = [new() { Symbols = [symbol] }] };

        List<SymbolSearchItem> items = new EplanSymbolReader().Read("project", new() { Language = language });

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(expected, item.Description));
        Assert.Equal(1, symbol.PropertyListReads);
        Assert.Equal(raw is null ? 0 : 1, symbol.StoredProperties.Reads.Count(id => id == Properties.Symbol.SYMB_DESC));
        Assert.Equal(1, symbol.StoredProperties.Disposals);
        Assert.All(symbol.StoredProperties.Values, value => Assert.True(value.Disposed));
    }

    [Fact]
    public void Description_ReadsNonFunctionSymbolsWithoutCategory()
    {
        var symbol = new Symbol { Type = Symbol.SymbolType.CableDefinitionLine, Variants = [new()] };
        symbol.StoredProperties.Description = "en_US@Cable definition;";
        EplanProjects.Project = new() { SymbolLibraries = [new() { Symbols = [symbol] }] };

        SymbolSearchItem item = Assert.Single(new EplanSymbolReader().Read("project", new()));

        Assert.Equal("Cable definition", item.Description);
        Assert.Null(item.FunctionCategory);
        Assert.Equal([Properties.Symbol.SYMB_DESC], symbol.StoredProperties.Reads);
        Assert.Equal(1, symbol.PropertyListReads);
        Assert.Equal(1, symbol.StoredProperties.Disposals);
        Assert.All(symbol.StoredProperties.Values, value => Assert.True(value.Disposed));
    }

    [Fact]
    public void Description_UnknownLanguage_IsRejected()
    {
        EplanProjects.Project = new();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => new EplanSymbolReader().Read("project", new() { Language = "xx_YY" })).Error.Reason);
    }

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
