using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DevicePlaceOperationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("IEC_symbol", "XTR1")]
    public void PartTerminal_UsesPartRouteAndExistingTerminalNaming(string? library, string? symbol)
    {
        _parts.Content.Parts.Add(new() { PartNumber = "PXC.3212140", Variant = "1" });
        var args = Args(NewPage, "", "PXC.3212140");
        args.Tag = null;
        args.TerminalStrip = "+A1-XQ10";
        args.TerminalNumber = "1";
        args.Library = library;
        args.Symbol = symbol;
        args.SymbolVariant = symbol is null ? null : 1;
        _placer.Result = tag => new() { MainName = tag, Names = [tag], OnPage = 1, Category = "Terminal", Terminal = new() { Strip = "+A1-XQ10", Number = "1" } };
        var symbols = new PartSymbols();
        WritePlan plan = (WritePlan)new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _placer, symbols).Execute(args)!;
        args.Fingerprint = plan.Fingerprint;
        DevicePlaceData result = (DevicePlaceData)new DevicePlaceExecuteOperation(_catalog, _pages, _names, _parts, _placer, _transactions, symbols).Execute(args)!;
        Assert.Equal("+A1-XQ10:1", result.Tag);
        Assert.Equal("PXC.3212140", Assert.Single(_placer.Places).Part);
        Assert.Equal("1", result.Terminal!.Number);
    }

    [Fact]
    public void PartTerminal_RunsInTheStripScope_BecauseASafetyPointCannotCreateTheStripDefinition()
    {
        // A13: Function.Create always throws S063176 in a SafetyPoint, whatever is locked first.
        _parts.Content.Parts.Add(new() { PartNumber = "PXC.3212140", Variant = "1" });
        var args = Args(NewPage, "", "PXC.3212140");
        args.Tag = null;
        args.TerminalStrip = "+A1-XQ10";
        args.TerminalNumber = "1";
        _placer.Result = tag => new() { MainName = tag, Names = [tag], OnPage = 1, Category = "Terminal", Terminal = new() { Strip = "+A1-XQ10", Number = "1" } };
        var symbols = new PartSymbols();
        args.Fingerprint = ((WritePlan)new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _placer, symbols).Execute(args)!).Fingerprint;
        new DevicePlaceExecuteOperation(_catalog, _pages, _names, _parts, _placer, _transactions, symbols).Execute(args);
        Assert.Contains("Commit", _placer.StripScopes.Calls);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void PartDevice_StaysInTheSafetyPointScope()
    {
        _parts.Content.Parts.Add(new() { PartNumber = "PXC.3212140", Variant = "1" });
        var args = Args(NewPage, "-Q20", "PXC.3212140");
        args.Fingerprint = Plan(args).Fingerprint;
        Execute(args);
        Assert.Contains("Commit", _transactions.Calls);
        Assert.Empty(_placer.StripScopes.Calls);
    }

    [Fact]
    public void PartSymbol_NotFoundRefusesBeforePlacement()
    {
        var args = Args(NewPage, "-Q20");
        args.Library = "IEC_symbol";
        args.Symbol = "MISSING";
        args.SymbolVariant = 0;
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _placer, new PartSymbols()).Execute(args));
        Assert.Equal(ErrorReason.SymbolNotFound, error.Error.Reason);
        Assert.Empty(_placer.Places);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void PartSymbol_PinOverridesRefuseBeforeAnyNativeWrite()
    {
        var args = Args(NewPage, "-Q20");
        args.Library = "IEC_symbol";
        args.Symbol = "FAH1";
        args.SymbolVariant = 0;
        args.ConnectionDesignations = ["1"];
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(args));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Contains("/display_properties:", error.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_placer.Places);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void PartTerminal_OccupiedNumberRefuses()
    {
        var args = Args(NewPage, "");
        args.Tag = null;
        args.TerminalStrip = "+A1-XQ10";
        args.TerminalNumber = "1";
        _names.Functions.Add(("+A1-XQ10:1", "9", false, NewPage, "1"));
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(args));
        Assert.Equal(ErrorReason.AlreadyExists, error.Error.Reason);
    }

    private sealed class PartSymbols : ISymbolReader
    {
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) =>
            [new() { Library = "IEC_symbol", Name = "XTR1", Variant = 1, Type = "Function", Kind = "function", FunctionCategory = "Terminal" }];
    }
}
