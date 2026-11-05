using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class SymbolPartPlacementTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BcpPart_IsAppendedAfterCommit_WithinOneUndo_AndFailureRollsBack(bool withPart, bool fail)
    {
        FakeMoveNative.Calls.Clear();
        FakeMoveNative.Value = 0;
        FakeMoveNative.EndTransaction = null;
        var page = new Page { Name = "=GAA+A1&EFS1/1" };
        var variant = new SymbolVariant { SymbolLibraryName = "SPECIAL", SymbolName = "BCP", VariantNr = 2 };
        EplanProjects.Project = new Project(page)
        { SymbolLibraries = [new() { Name = "SPECIAL", Symbols = [new() { Name = "BCP", Variants = [variant] }] }] };
        var args = new DevicePlaceArgs
        {
            Library = "SPECIAL",
            Symbol = "BCP",
            SymbolVariant = 2,
            Tag = "+A1-WE1",
            JoinDevice = true,
            Part = withPart ? "RIT.3451500" : null,
            Variant = withPart ? "1" : null,
            X = 80,
            Y = 160
        };
        var adapter = new EplanDeviceSymbolPlacement("project", page.Name,
            new() { Library = "SPECIAL", Name = "BCP", Variant = 2, Kind = "function", Type = "Function" }, withPart);
        Function? placed = null;
        DevicePlaceData Change()
        {
            DevicePlaceData data = adapter.Place(args, args.Tag!);
            placed = Assert.Single(page.AllPlacements.OfType<Function>());
            Assert.Empty(placed.ArticleAdds);
            if (fail)
            { placed.AddArticleError = new InvalidOperationException("S063074"); }
            FakeMoveNative.Value = 1;
            return data;
        }
        if (fail)
        {
            BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(adapter,
                "eplan_device_place", Change, _ => true, data => adapter.ReadCommitted(args, data)));
            Assert.Equal(ExecutionState.RolledBack, failure.Error.Execution);
            Assert.Contains("DoUndo", FakeMoveNative.Calls);
            Assert.DoesNotContain("CloseUndo", FakeMoveNative.Calls);
            Assert.Equal(0, FakeMoveNative.Value);
            return;
        }
        var result = TransactionRunner.Run(adapter, "eplan_device_place", Change, _ => true,
            data => adapter.ReadCommitted(args, data));
        Assert.Equal(withPart ? "RIT.3451500" : "", result.Result.Part);
        Assert.False(placed!.IsMainFunction);
        Assert.Equal(withPart ? 1 : 0, placed.ArticleAdds.Count);
        if (withPart)
        {
            Assert.Equal(("RIT.3451500", "1", 1u), placed.ArticleAdds[0]);
            int firstCommit = FakeMoveNative.Calls.IndexOf("Commit");
            int appendTransaction = FakeMoveNative.Calls.LastIndexOf("Transaction");
            Assert.True(firstCommit < appendTransaction);
            Assert.True(FakeMoveNative.Calls.LastIndexOf("Commit") < FakeMoveNative.Calls.IndexOf("CloseUndo"));
        }
        Assert.Single(FakeMoveNative.Calls, call => call == "CloseUndo");
    }
}
