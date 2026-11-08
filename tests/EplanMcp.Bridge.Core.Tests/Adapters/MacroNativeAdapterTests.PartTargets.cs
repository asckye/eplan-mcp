using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using NativeInsert = Eplan.EplApi.HEServices.Insert;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Fake native objects are project-owned; their Dispose rejects borrowed-object disposal.

public sealed partial class MacroNativeAdapterTests
{
    [Fact]
    public void SiemensCpuOverview_AssignsCpuPartOnlyToMainDeviceAndRetainsBusAdapterPart()
    {
        Page page = ConfigureMacro();
        var cpu = Function(page, 1);
        cpu.Name = "=K1+UC1-K1";
        var busAdapter = Function(page, 2);
        busAdapter.Name = "=K1+UC1-K1-K1";
        NativeInsert.Place = target =>
        {
            target.AllPlacements = [cpu, busAdapter];
            return [cpu, busAdapter];
        };

        PlacedMacro placed = new EplanMacroInserter().Insert(
            "project", page.Name, "CPU.ema", 0, 20, 296, true, "Overview", "6ES7510-1SK03-0AB0", "1");

        Assert.Equal(("6ES7510-1SK03-0AB0", "1", 1u), Assert.Single(cpu.ArticleAdds));
        Assert.Empty(busAdapter.ArticleAdds);
        Assert.Equal(cpu.Name, Assert.Single(placed.ArticleReferences).Tag);

        cpu.ArticleReferences = [];
        busAdapter.ArticleReferences = [new() { PartNr = "BusAdapter.Part", VariantNr = "1", Count = 1 }];
        page.AllPlacements = [];

        PlacedMacro withExistingPart = new EplanMacroInserter().Insert(
            "project", page.Name, "CPU.ema", 0, 20, 296, true, "Overview", "6ES7510-1SK03-0AB0", "1");

        Assert.Empty(busAdapter.ArticleAdds);
        Assert.Equal(["6ES7510-1SK03-0AB0", "BusAdapter.Part"], withExistingPart.ArticleReferences.Select(article => article.PartNumber));
    }
}
