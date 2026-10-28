using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Borrowed fake EPLAN objects must never be disposed (A39/A55). No native resources.")]
public sealed class LocationBoxPlacementTests
{
    private readonly Function _coil = new(1) { Name = "=F+A1-K1" };
    private readonly Page _page = new() { Name = "=F+A1/1" };
    private readonly GraphicalPlacement _outline = new(3) { Bounds = [new(60, 140), new(100, 180)] };
    private readonly LocationBox _box;
    private readonly EplanDeviceSymbolPlacement _adapter;

    public LocationBoxPlacementTests()
    {
        _page.AllPlacements = [_coil];
        _box = new(2) { Page = _page, SymbolVariant = new() { Library = "SPECIAL", Name = "SC", Variant = 0 }, Graphics = new Group(4, _outline) };
        LocationBox.Factory = (_, _) => _box;
        _adapter = new(new Project(_page), _page);
    }

    [Fact]
    public void NativeFactory_CreatesDashedBoxAndKeepsExistingDeviceName_ListsReleased()
    {
        DevicePlaceData result = _adapter.PlaceBox(Args());
        Assert.Equal("+P1", result.LocationBox!.Structure);
        Assert.Equal([60d, 140d, 100d, 180d], result.LocationBox.Bounds);
        Assert.Equal("dashed", result.LocationBox.Outline);
        Assert.Empty(result.LocationBox.AffectedDevices);
        Assert.Equal("=F+A1-K1", _coil.Name);
        Assert.Equal(1, _outline.Pen.StyleId);
        Assert.True(_box.AssignedList!.Disposed);
        Assert.True(_box.ReadList!.Disposed);
    }

    [Theory]
    [InlineData("factory")]
    [InlineData("graphics")]
    [InlineData("pen")]
    [InlineData("area")]
    [InlineData("device")]
    [InlineData("outside")]
    [InlineData("unknownBounds")]
    [InlineData("structure")]
    [InlineData("symbol")]
    public void NativeReadbackMismatch_RejectsBeforeCommitAndReleasesAcquiredLists(string mismatch)
    {
        switch (mismatch)
        {
            case "factory":
                LocationBox.Factory = (_, _) => _coil;
                break;
            case "graphics":
                _box.Graphics = null;
                break;
            case "pen":
                _outline.RejectStyle = true;
                break;
            case "area":
                _box.WrongArea = true;
                break;
            case "device":
                _outline.OnSet = () => _coil.Name = "=F+P1-K1";
                break;
            case "unknownBounds":
                _outline.Bounds = [];
                break;
            case "outside":
                _outline.Bounds = [new(60, 140), new(500, 180)];
                break;
            case "structure":
                _outline.OnSet = () => _box.LocationName = "WRONG";
                break;
            case "symbol":
                _box.SymbolVariant.Name = "WRONG";
                break;
        }
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => _adapter.PlaceBox(Args()));
        Assert.Equal(ErrorReason.EplanOperationFailed, error.Error.Reason);
        if (_box.AssignedList is { } assigned)
        { Assert.True(assigned.Disposed); }
        if (_box.ReadList is { } read)
        { Assert.True(read.Disposed); }
    }

    [Fact]
    public void CancelDuringOutlineWrite_AbortsWithoutClosingUndoAndRestoresExistingStructure()
    {
        var context = new OperationContext("box", write: true, null);
        _outline.OnSet = context.Cancel;
        var transactions = new FakeTransactions { Snapshot = () => () => _page.AllPlacements = [_coil] };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => context.Run(() => TransactionRunner.Run(transactions, "eplan_device_place", () => _adapter.PlaceBox(Args()), _ => true)));
        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.DoesNotContain("Commit", transactions.Calls);
        Assert.DoesNotContain("CloseUndo", transactions.Calls);
        Assert.Equal("=F+A1-K1", _coil.Name);
        Assert.True(_box.ReadList!.Disposed);
    }

    private static DevicePlaceArgs Args() => new() { LocationBox = "+P1", X = 60, Y = 140, CornerX = 100, CornerY = 180 };
}
