using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementLanguageRegressionTests
{
    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void EachLanguage_HasItsOwnCharacterLimit(int length, bool accepted)
    {
        var store = new PlacementTextStore();
        PlacementTextCreateArgs args = PlacementTextStore.Args();
        args.Text.Contents = new() { ["en_US"] = new('a', length), ["de_DE"] = new('b', length) };
        if (accepted)
        {
            PlacementTextCreateData result = Assert.IsType<PlacementTextCreateData>(store.Operation.Execute(args));
            Assert.Equal(args.Text.Contents, result.Text.Contents);
            Assert.Equal(["Begin AI: eplan_placement_create", "Commit", "CloseUndo", "Dispose"], store.Transactions.Calls);
        }
        else
        {
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(args)).Error.Reason);
            Assert.Empty(store.Transactions.Calls);
        }
    }

    [Fact]
    public void Warn_PropagatesAdapterLanguageWarningToPlanAndExecution()
    {
        var store = new PlacementTextStore { ValidationWarning = "Missing project display language (A54)." };
        PlacementTextCreateArgs args = PlacementTextStore.Args();
        args.LayoutGuard = "warn";
        var plan = new PlacementTextCreateExecuteOperation(new FakeCatalog(store.Project), store, store.Transactions, store, planning: true);
        Assert.Contains(store.ValidationWarning, Assert.IsType<PlacementTextCreateData>(plan.Execute(args)).Warning, StringComparison.Ordinal);
        Assert.Empty(store.Transactions.Calls);
        Assert.Contains(store.ValidationWarning, Assert.IsType<PlacementTextCreateData>(store.Operation.Execute(args)).Warning, StringComparison.Ordinal);
        Assert.Single(store.Texts);
    }
}
