using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Borrowed fake EPLAN objects; no native resources.")]
public sealed class TerminalPlacementTests : IDisposable
{
    private readonly Page _page = new() { Name = "=F+A1/1" };
    private readonly EplanDeviceSymbolPlacement _adapter;
    public TerminalPlacementTests() { Terminal.OnCreate = null; _adapter = new(new Project(_page), _page); }
    public void Dispose() => Terminal.OnCreate = null;

    [Fact]
    public void Create_ParentPrecedesTerminal_AndNativeReadbackPreservesAssignedSortCode()
    {
        Terminal.OnCreate = terminal => Assert.IsType<TerminalStrip>(_page.AllPlacements[0]);
        DevicePlaceData data = _adapter.PlaceTerminal(Args(), "=F+A1-X6:1");
        TerminalStrip strip = Assert.IsType<TerminalStrip>(_page.AllPlacements[0]);
        Terminal terminal = Assert.IsType<Terminal>(_page.AllPlacements[1]);
        Assert.Same(strip, terminal.TerminalStrip);
        Assert.Equal(("=F+A1-X6", "1", "17", true), (data.Terminal!.Owner, data.Terminal.Number, data.Terminal.SortCode, data.Terminal.StripCreated));
        Assert.Equal([80d, 160d], data.Terminal.Position);
        Assert.Equal("??_??@Control box;", strip.Properties.FUNC_TEXT.ToString());
        Assert.True(strip.Properties.Disposed);
        Assert.True(terminal.Properties.Disposed);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("category")]
    [InlineData("part")]
    [InlineData("variant")]
    public void Create_NativeMismatch_ReleasesListsBeforeAbortAndRestoresBothObjects(string mismatch)
    {
        Terminal? created = null;
        Terminal.OnCreate = terminal =>
        {
            created = terminal;
            switch (mismatch)
            {
                case "owner":
                    terminal.ClearOwner();
                    break;
                case "category":
                    terminal.FunctionCategory = "PLCTerminal";
                    break;
                case "part":
                    terminal.ArticleReferences = [new()];
                    break;
                case "variant":
                    terminal.SymbolVariant.Variant = 3;
                    break;
            }
        };
        var transactions = new FakeTransactions { Snapshot = () => () => { Assert.True(created!.Properties.Disposed); _page.AllPlacements = []; } };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(transactions, "eplan_device_place", () => _adapter.PlaceTerminal(Args(), "=F+A1-X6:1"), _ => true));
        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Empty(_page.AllPlacements);
        Assert.DoesNotContain("CloseUndo", transactions.Calls);
    }

    [Fact]
    public void Create_Cancellation_ReleasesListsAndAbortsWithoutEmptyUndo()
    {
        var context = new OperationContext("terminal", write: true, null);
        Terminal.OnCreate = _ => context.Cancel();
        var transactions = new FakeTransactions { Snapshot = () => () => _page.AllPlacements = [] };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => context.Run(() => TransactionRunner.Run(transactions, "eplan_device_place", () => _adapter.PlaceTerminal(Args(), "=F+A1-X6:1"), _ => true)));
        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Empty(_page.AllPlacements);
        Assert.DoesNotContain("CloseUndo", transactions.Calls);
    }

    [Fact]
    public void Create_ReusesNativeParent_AndRefusesAnOrdinaryDeviceAtTheParentName()
    {
        var strip = new TerminalStrip { Name = "=F+A1-X6", IsMainFunction = true };
        _page.AllPlacements = [strip];
        DevicePlaceArgs args = Args();
        args.TerminalStripText = null;
        args.TerminalNumber = "2";
        Assert.False(_adapter.PlaceTerminal(args, "=F+A1-X6:2").Terminal!.StripCreated);
        Assert.Single(_page.AllPlacements.OfType<TerminalStrip>());
#pragma warning disable CA2000 // Borrowed fake functions must not be disposed (A39/A55).
        _page.AllPlacements = [new Function(1) { Name = strip.Name }];
#pragma warning restore CA2000
        Assert.Throws<BridgeErrorException>(() => _adapter.PlaceTerminal(args, "=F+A1-X6:2"));
        Assert.Single(_page.AllPlacements);
    }

    private static DevicePlaceArgs Args() => new() { Page = "=F+A1/1", TerminalStrip = "-X6", TerminalNumber = "1", TerminalStripText = "Control box", X = 80, Y = 160 };
}
