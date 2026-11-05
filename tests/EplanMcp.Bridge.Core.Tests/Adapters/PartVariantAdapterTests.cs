using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Persistent native objects belong to the fake project.
[Trait("Size", "Small")]
public sealed class PartVariantAdapterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(7)]
    public void TemplateSymbol_ChangesOnlyVariant_OrRollsBackUnknownVariant(int? variant)
    {
        var page = new Page();
        var main = new Function { Page = page, IsMainFunction = true };
        main.SymbolVariant.Library = "IEC_symbol";
        main.SymbolVariant.Name = "X3";
        var definition = main.FunctionDefinition;
        var project = new Project(page)
        {
            SymbolLibraries = [new() { Name = "IEC_symbol", Symbols = [new() { Name = "X3", Variants =
                [new SymbolVariant { SymbolLibraryName = "IEC_symbol", SymbolName = "X3" }, new SymbolVariant { SymbolLibraryName = "IEC_symbol", SymbolName = "X3", VariantNr = 1 }] }] }]
        };
        EplanProjects.Project = project;
        DeviceService.Create = (part, _, _, _) => { Assert.Equal("PXC.3211813", part); page.AllPlacements = [main]; return [main]; };
        var transactions = new FakeTransactions { Snapshot = () => () => page.AllPlacements = [] };
        try
        {
            PlacedDevice Place() => new EplanDevicePlacer().Place("project", page.Name, "PXC.3211813", "1", 80, 160, "", new() { Part = "PXC.3211813", SymbolVariant = variant });
            if (variant == 7)
            {
                BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(transactions, "eplan_device_place", Place));
                Assert.Equal(ExecutionState.RolledBack, failure.Error.Execution);
                Assert.Contains("Available variants: 0, 1", failure.Error.Message, StringComparison.Ordinal);
                Assert.Empty(page.AllPlacements);
            }
            else
            {
                PlacedDevice result = TransactionRunner.Run(transactions, "eplan_device_place", Place).Result;
                Assert.Equal(variant, result.SymbolVariant);
                Assert.Equal(("IEC_symbol", "X3", variant ?? 0), (main.SymbolVariant.SymbolLibraryName, main.SymbolVariant.SymbolName, main.SymbolVariant.VariantNr));
                Assert.Same(definition, main.FunctionDefinition);
                Assert.Single(transactions.Calls, call => call == "CloseUndo");
            }
        }
        finally { EplanProjects.Project = null; DeviceService.Create = null; }
    }
}
