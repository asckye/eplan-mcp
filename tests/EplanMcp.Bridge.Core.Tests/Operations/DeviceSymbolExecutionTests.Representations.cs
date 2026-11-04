using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DeviceSymbolExecutionTests
{
    [Theory]
    [InlineData("1.1", "Voltage US", "1", 104)]
    [InlineData("RJ45", "ETHERNET", "X1", 40)]
    public void Report1339And1340_ExistingPointCanBePlannedAsAnotherRepresentation(string point, string text, string plug, int x)
    {
        // Report inputs retain symbol, point, plug, page and geometry; the local project path is replaced.
        var args = new DevicePlaceArgs
        {
            Project = "Line1",
            Page = "=K1+A2&EFS1/2",
            Library = "IEC_symbol",
            Symbol = "PLC_CBOX_PCON_PLUG",
            SymbolVariant = 0,
            X = x,
            Y = 92,
            Tag = "=K1+A2-KE1",
            ConnectionDesignations = [point],
            PlcConnectionPoint = point,
            PlcText = text,
            PlcPlugDesignation = plug
        };
        _pages.Pages.Add(new() { Name = args.Page });
        _names.Functions.Add((args.Tag, "card", true, "=K1+A2&EFA1/4", "KE1"));
        _names.Functions.Add((args.Tag + ":" + point, "overview", false, "=K1+A2&EFA1/4", "KE1:" + point));
        _source.Symbol.Name = args.Symbol;
        _source.Symbol.FunctionCategory = "PLCTerminal";
        _source.Symbol.PlcInputDefinition = "1/2";
        WritePlan plan = Assert.IsType<WritePlan>(new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _source, _source, _source).Execute(args));
        Assert.Equal(args.Tag + ":" + point, plan.Details!["tag"]);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData("PLCTerminal", true)]
    [InlineData("BusTerminal", true)]
    [InlineData("Coil", false)]
    [InlineData("Terminal", false)]
    [InlineData(null, true)]
    public void ReusesPoint_OnlySamePlcNameWithEligibleCategoryOrUnreadableCategory(string? category, bool allowed)
    {
        const string name = "=K1+A2-KE1:1.1";
        var device = new DeviceNames { Functions = [new() { Name = name, Category = category }] };
        Assert.Equal(allowed, DevicePlcPlan.ReusesPoint(name, [name], device, out string? warning));
        Assert.Equal(category is null, warning is not null);
        Assert.False(DevicePlcPlan.ReusesPoint(name, [name + ":other"], device, out _));
        Assert.False(DevicePlcPlan.ReusesPoint(name, [], device, out _));
    }
}
