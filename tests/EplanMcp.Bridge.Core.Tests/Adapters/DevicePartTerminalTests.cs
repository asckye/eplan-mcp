using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Borrowed fake native objects match EPLAN ownership.
[Trait("Size", "Small")]
public sealed class DevicePartTerminalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatedTerminal_GetsFullNameAndOptionalSymbol(bool replace)
    {
        var page = new Page { Name = "=F+A1/1" };
        var project = new Project(page);
        project.FunctionDefinitionLibrary.Definitions = [new() { FunctionCategory = FunctionCategory.TerminalDefText }];
        var variant = new SymbolVariant { SymbolName = "XTR1", VariantNr = 1 };
        project.SymbolLibraries = [new() { Symbols = [new() { Name = "XTR1", Variants = [variant] }] }];
        var terminal = new Terminal { Name = "+-X1", Page = page, IsPlaced = true, Location = new(80, 160), FunctionCategory = FunctionCategory.Terminal };
        terminal.SymbolVariant.Library = "IEC_symbol";
        terminal.SymbolVariant.Name = "X2_2";
        EplanProjects.Project = project;
        NameService.Adjust = (target, function) => { Assert.Same(page, target); Assert.Equal("+A1-XQ10:1", function.Name); ((Function)function).VisibleName = "-XQ10"; return true; };
        DeviceService.Create = (part, _, _, _) => { Assert.Equal("PXC.3212140", part); page.AllPlacements = [terminal]; return [terminal]; };
        try
        {
            var args = new DevicePlaceArgs { Part = "PXC.3212140", TerminalStrip = "+A1-XQ10", TerminalNumber = "1", X = 80, Y = 160, Library = replace ? "IEC_symbol" : null, Symbol = replace ? "XTR1" : null, SymbolVariant = replace ? 1 : null };
            var placer = new EplanDevicePlacer();
            PlacedDevice result = placer.Place("project", page.Name, args.Part, "1", args.X, args.Y, "+A1-XQ10:1", args);
            Assert.Equal("+A1-XQ10:1", result.MainName);
            Assert.Equal("Terminal", result.Category);
            Assert.Equal("-XQ10", result.VisibleName);
            Assert.False(terminal.IsMainFunction);
            Assert.Equal(replace ? "XTR1" : "X2_2", terminal.SymbolVariant.Name);
            Assert.Equal(replace ? 1 : 0, terminal.SymbolVariant.Variant);
            Assert.True(result.Terminal!.StripCreated);
            page.AllPlacements = [new Terminal { Name = result.MainName, Page = page, FunctionCategory = FunctionCategory.Terminal }, new Terminal { Name = "+A1-XQ10:2", Page = page }];
            terminal.CheckNative = () => throw new InvalidOperationException("Old native wrapper reused.");
            project.OmitTerminalsFromFunctions = true;
            placer.ReadCommitted("project", result);
            Assert.Equal("+A1-XQ10:1", Assert.Single(result.Names));
        }
        finally { EplanProjects.Project = null; DeviceService.Create = null; NameService.Adjust = null; }
    }
}
