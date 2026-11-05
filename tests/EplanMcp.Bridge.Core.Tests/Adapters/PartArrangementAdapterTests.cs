using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Persistent fake functions have the same ownership as the native objects.
[Trait("Size", "Small")]
public sealed class PartArrangementAdapterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("左,0°")]
    [InlineData("MISSING")]
    public void PartArrangement_SelectsAndReadsWithinPlacement_UnknownNameRollsBack(string? arrangement)
    {
        var page = new Page();
        var main = new Function { Page = page, IsMainFunction = true };
        main.PropertyPlacementsSchemas.All = [new("左,0°"), new("右,0°")];
        FunctionPropertyList values = main.Properties;
        main.PropertyPlacementsSchemas.OnSelect = selected => values[(Properties.Function)19307] = selected.Name;
        EplanProjects.Project = new Project(page);
        DeviceService.Create = (_, _, _, _) => { page.AllPlacements = [main]; return [main]; };
        var transactions = new FakeTransactions { Snapshot = () => () => page.AllPlacements = [] };
        try
        {
            PlacedDevice Place() => new EplanDevicePlacer().Place("project", page.Name, "P", "1", 80, 160, "", new() { PropertyArrangement = arrangement });
            if (arrangement == "MISSING")
            {
                BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(transactions, "eplan_device_place", Place));
                Assert.Equal(ExecutionState.RolledBack, failure.Error.Execution);
                Assert.Contains("左,0°", failure.Error.Message, StringComparison.Ordinal);
                Assert.Empty(page.AllPlacements);
            }
            else
            {
                PlacedDevice result = TransactionRunner.Run(transactions, "eplan_device_place", Place).Result;
                Assert.Equal(arrangement, result.PropertyArrangement);
                Assert.Equal(arrangement, result.ActivePropertySet);
                Assert.Single(transactions.Calls, call => call == "CloseUndo");
            }
        }
        finally { EplanProjects.Project = null; DeviceService.Create = null; }
    }
}
