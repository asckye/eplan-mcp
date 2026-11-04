using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class BlackBoxPlacementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommittedDevicePoint_ReadsNativeContainmentWithoutRejectingOutside(bool outside)
    {
        const string tag = "=GAA+A1-PF9";
        var page = new Page { Name = "=GAA+A1&EFS1/1" };
        EplanProjects.Project = new Project(page);
#pragma warning disable CA2000 // Native test placements are borrowed from the page and must never be disposed.
        var box = new BoxedDevice();
        box.Create(page);
        box.SymbolVariant = new() { SymbolLibraryName = "SPECIAL", SymbolName = "DC" };
        box.Name = tag;
        box.SetLogicalArea(new(60, 80), new(120, 120));
        var point = new Function(41)
        {
            Name = tag + ":1",
            FunctionCategory = global::Eplan.EplApi.Base.Enums.FunctionCategory.DeviceEndTerminal,
            Location = outside ? new(180, 180) : new(80, 100)
        };
#pragma warning restore CA2000
        point.Create(page, new() { SymbolLibraryName = "SPECIAL", SymbolName = "DCP" });
        var adapter = new EplanDeviceSymbolPlacement("project", page.Name,
            new() { Library = "SPECIAL", Name = "DCP", Type = "Function", Kind = "function", FunctionCategory = "DeviceEndTerminal", Variant = 0 });
        typeof(EplanDeviceSymbolPlacement).GetField("_placedFunctionId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(adapter, (long?)41);
        var args = new DevicePlaceArgs { Library = "SPECIAL", Symbol = "DCP", SymbolVariant = 0, Tag = tag, X = point.Location.X, Y = point.Location.Y };
        var result = new DevicePlaceData { Page = page.Name };
        adapter.ReadCommitted(args, result);
        Assert.Equal(tag + ":1", result.Tag);
        Assert.Equal("DeviceEndTerminal", result.Category);
        Assert.False(result.IsMainFunction);
        if (outside)
        { Assert.Null(result.BlackBox); }
        else
        {
            Assert.Equal(tag, result.BlackBox!.Tag);
            Assert.Equal([60d, 80d, 120d, 120d], result.BlackBox.Bounds);
            Assert.Equal([tag + ":1"], result.BlackBox.FunctionsInside!);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeBlackBox_UsesFactoryAndChecksLogicalArea(bool wrongArea)
    {
        const string tag = "=GAA+A1-PF9";
        var page = new Page { Name = "=GAA+A1&EFS1/1" };
        var variant = new SymbolVariant { SymbolLibraryName = "SPECIAL", SymbolName = "DC", VariantNr = 0 };
        EplanProjects.Project = new Project(page)
        {
            SymbolLibraries = [new() { Name = "SPECIAL", Symbols = [new() { Name = "DC", Type = Symbol.SymbolType.BoxFunction, Variants = [variant] }] }]
        };
        var adapter = new EplanDeviceSymbolPlacement("project", page.Name,
            new() { Library = "SPECIAL", Name = "DC", Type = "BoxFunction", Kind = "function", FunctionCategory = "Blackbox", Variant = 0 });
        var args = new DevicePlaceArgs { Library = "SPECIAL", Symbol = "DC", SymbolVariant = 0, Tag = tag, X = 60, Y = 80, CornerX = 120, CornerY = 120 };
        BoxedDevice.OnCreate = box => box.WrongArea = wrongArea;
        try
        {
            if (wrongArea)
            {
                BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => adapter.Place(args, tag));
                Assert.Equal(ErrorReason.EplanOperationFailed, error.Error.Reason);
                return;
            }
            DevicePlaceData result = adapter.Place(args, tag);
            Assert.Equal(tag, result.Tag);
            Assert.Equal("Blackbox", result.Category);
            BoxedDevice box = Assert.Single(page.AllPlacements.OfType<BoxedDevice>());
            Assert.True(box.IsMainFunction);
            Assert.Equal("DC", box.SymbolVariant.SymbolName);
            Assert.Equal(new PointD(60, 80), box.GetLogicalArea().LowerLeft);
            Assert.Equal(new PointD(120, 120), box.GetLogicalArea().UpperRight);
            adapter.ReadCommitted(args, result);
            Assert.Equal("BoxedDevice", result.Type);
            Assert.True(result.IsMainFunction);
            Assert.Equal(tag, result.BlackBox!.Tag);
            Assert.Equal([60d, 80d, 120d, 120d], result.BlackBox.Bounds);
            Assert.Empty(result.BlackBox.FunctionsInside!);
        }
        finally { BoxedDevice.OnCreate = null; }
    }
}

