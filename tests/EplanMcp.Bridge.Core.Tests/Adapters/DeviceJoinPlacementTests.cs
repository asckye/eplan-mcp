using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class DeviceJoinPlacementTests
{
    [Theory]
    [InlineData("DC")]
    [InlineData("BBDEF")]
    public void SpecialSymbol_JoinCreatesAuxiliaryAndReadsItBackAfterCommit(string symbol)
    {
        const string tag = "=GAA+A1-KJ1";
        var page = new Page { Name = "=GAA+A1&EFS1/1" };
        var variant = new SymbolVariant { SymbolLibraryName = "SPECIAL", SymbolName = symbol, VariantNr = 0 };
        EplanProjects.Project = new Project(page)
        {
            SymbolLibraries = [new() { Name = "SPECIAL", Symbols = [new() { Name = symbol, Variants = [variant] }] }]
        };
#pragma warning disable CA2000 // Persistent native test placements belong to their page.
        var original = new Function(41) { Name = tag, IsMainFunction = true };
#pragma warning restore CA2000
        original.Create(page, variant);
        var adapter = new EplanDeviceSymbolPlacement("project", page.Name,
            new() { Library = "SPECIAL", Name = symbol, Kind = "function", Type = symbol == "DC" ? "BoxFunction" : "TextFunction" });
        var args = new DevicePlaceArgs
        {
            Library = "SPECIAL",
            Symbol = symbol,
            SymbolVariant = 0,
            Tag = tag,
            JoinDevice = true,
            X = 80,
            Y = 160,
            CornerX = symbol == "DC" ? 120 : null,
            CornerY = symbol == "DC" ? 200 : null
        };

        DevicePlaceData result = adapter.Place(args, tag);

        Function placed = Assert.Single(page.AllPlacements.OfType<Function>(), f => f.ObjectIdentifier != original.ObjectIdentifier);
        Assert.False(placed.IsMainFunction);
        Assert.False(result.IsMainFunction);
        if (symbol == "BBDEF")
        {
            // This fake has no symbol-category factory; supply the native BBDEF category for the committed readback.
            placed.FunctionCategory = global::Eplan.EplApi.Base.Enums.FunctionCategory.BusBarDefText;
        }
        adapter.ReadCommitted(args, result);
        Assert.Equal(tag, result.Tag);
        Assert.False(result.IsMainFunction);
        Assert.Equal(symbol == "DC" ? "Blackbox" : "BusBarDefText", result.Category);
        Assert.True(original.IsMainFunction);
        Assert.Equal(tag, original.Name);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void OrdinarySymbol_JoinRetainsNameAndCreatesOnlyAnAuxiliaryFunction(bool? join, bool main)
    {
        const string tag = "=GAA+A1-KJ1";
        var page = new Page { Name = "=GAA+A1&EFS1/1" };
        var variant = new SymbolVariant { SymbolLibraryName = "IEC_symbol", SymbolName = "K", VariantNr = 0 };
        EplanProjects.Project = new Project(page)
        {
            SymbolLibraries = [new() { Name = "IEC_symbol", Symbols = [new() { Name = "K", Variants = [variant] }] }]
        };
#pragma warning disable CA2000 // Persistent native test placements belong to their page.
        var original = new Function(41) { Name = tag, IsMainFunction = true };
#pragma warning restore CA2000
        original.Create(page, variant);
        var adapter = new EplanDeviceSymbolPlacement("project", page.Name,
            new() { Library = "IEC_symbol", Name = "K", Kind = "function", Type = "Function" });
        var args = new DevicePlaceArgs { Library = "IEC_symbol", Symbol = "K", SymbolVariant = 0, Tag = tag, JoinDevice = join, X = 80, Y = 160 };

        DevicePlaceData result = adapter.Place(args, tag);

        Function placed = Assert.Single(page.AllPlacements.OfType<Function>(), f => f.ObjectIdentifier != original.ObjectIdentifier);
        Assert.Equal(tag, placed.Name);
        Assert.Equal(main, placed.IsMainFunction);
        if (join == true)
        { Assert.False(result.IsMainFunction); }
        else
        { Assert.Null(result.IsMainFunction); }
        Assert.True(original.IsMainFunction);
        Assert.Equal(tag, original.Name);
        if (join == true)
        {
            adapter.ReadCommitted(args, result);
            Assert.Equal(tag, result.Tag);
            Assert.False(result.IsMainFunction);
        }
    }
}
