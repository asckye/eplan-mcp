using Eplan.EplApi.Base.Enums;
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
    private readonly Project _project;
    public TerminalPlacementTests()
    {
        Terminal.OnCreate = null;
        _project = new Project(_page);
        _project.FunctionDefinitionLibrary.Definitions = [new() { FunctionCategory = FunctionCategory.Terminal }, new() { FunctionCategory = FunctionCategory.TerminalDefText }];
        _adapter = new(_project, _page);
    }
    public void Dispose() => Terminal.OnCreate = null;

    [Fact]
    public void PlacedDefinition_SetsIndependentPositionAndText_KeepsTheDefaultArrangement()
    {
        DevicePlaceArgs args = Args();
        args.TerminalStripPosition = [72, 120];
        DevicePlaceData result = _adapter.PlaceTerminal(args, "=F+A1-X6:1");
        TerminalStrip strip = Assert.Single(_page.AllPlacements.OfType<TerminalStrip>());
        Assert.Equal([72d, 120d], result.Terminal!.StripPosition!);
        Assert.Equal("Control box", result.Terminal.StripText);
        // Customer redraw 2026-11-02: placed as SPECIAL TDEF like EPLAN's insert command; no texts are created or undocked.
        Assert.Equal(("SPECIAL", "TDEF"), (strip.SymbolVariant.Library, strip.SymbolVariant.Name));
        Assert.Empty(strip.PropertyPlacements);
    }

    [Fact]
    public void PlacedDefinition_UsesRequestedSpecialSymbolAndVariant()
    {
        _project.SymbolLibraries[0].Symbols = [new() { Name = "TEST_DEF", Variants = [new() { SymbolLibraryName = "SPECIAL", SymbolName = "TEST_DEF", VariantNr = 3 }] }];
        DevicePlaceArgs args = Args();
        args.TerminalStripPosition = [72, 120];
        args.StripDefinition = new() { Symbol = "TEST_DEF", Variant = 3 };
        _adapter.PlaceTerminal(args, "=F+A1-X6:1");
        TerminalStrip strip = Assert.Single(_page.AllPlacements.OfType<TerminalStrip>());
        Assert.Equal(("SPECIAL", "TEST_DEF", 3), (strip.SymbolVariant.Library, strip.SymbolVariant.Name, strip.SymbolVariant.Variant));
    }

    [Fact]
    public void PlacedDefinition_RefusesOtherLibraries()
    {
        DevicePlaceArgs args = Args();
        args.TerminalStripPosition = [72, 120];
        args.StripDefinition = new() { Library = "IEC_symbol" };
        BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => _adapter.PlaceTerminal(args, "=F+A1-X6:1"));
        Assert.Contains("only support the SPECIAL library", failure.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_page.AllPlacements);
    }

    [Fact]
    public void UnplacedDefinition_DoesNotUsePluginSelection()
    {
        DevicePlaceArgs args = Args();
        args.StripDefinition = new() { Symbol = "MISSING" };
        Assert.True(_adapter.PlaceTerminal(args, "=F+A1-X6:1").Terminal!.StripCreated);
        Assert.Empty(_page.AllPlacements.OfType<TerminalStrip>());
    }

    [Fact]
    public void Create_ParentPrecedesTerminal_AndNativeReadbackPreservesAssignedSortCode()
    {
        TerminalStrip? parent = null;
        Terminal.OnCreate = terminal => parent = terminal.TerminalStrip;
        DevicePlaceData data = _adapter.PlaceTerminal(Args(), "=F+A1-X6:1");
        // The strip definition is created unplaced (acceptance trial: Create(Page) put it at the page origin).
        Terminal terminal = Assert.IsType<Terminal>(Assert.Single(_page.AllPlacements));
        TerminalStrip strip = Assert.IsType<TerminalStrip>(parent);
        Assert.Same(_project, strip.DefinedIn);
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
                case "owner": // a null owner is a relation pending commit (S43, live T7c)
                    terminal.ForeignOwner();
                    break;
                case "category":
                    terminal.FunctionCategory = FunctionCategory.PLCTerminal;
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
    public void Create_PendingRelation_IsReturnedAsNullUntilCommit()
    {
        Terminal.OnCreate = terminal => terminal.ClearOwner();
        DevicePlaceData data = _adapter.PlaceTerminal(Args(), "=F+A1-X6:1");
        Assert.Null(data.Terminal!.Owner);
        Assert.Equal("=F+A1-X6", data.Terminal.Strip);
        Assert.True(Assert.IsType<Terminal>(Assert.Single(_page.AllPlacements)).Properties.Disposed);
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
#pragma warning disable CA2000 // Project-owned fake parent must not be disposed (A39).
        var strip = new TerminalStrip { Name = "=F+A1-X6", IsMainFunction = true };
#pragma warning restore CA2000
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
