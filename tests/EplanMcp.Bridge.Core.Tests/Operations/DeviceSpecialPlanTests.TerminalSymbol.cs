using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DeviceSpecialPlanTests
{
    [Theory]
    [InlineData("IEC_symbol", "X2_2", 0)]
    [InlineData("GRAPHICS", "KA1_PE", 0)]
    [InlineData("CUSTOM", "AnyTerminal", 2)]
    public void TerminalSymbol_PreviewUsesDiscoveredTerminalCategoryWithoutANameAllowlist(string library, string name, int variant)
    {
        SetSymbol(library, name, variant, "Function", "function", "Terminal");
        DevicePlaceArgs args = TerminalArgs();
        args.Library = library;
        args.Symbol = name;
        args.SymbolVariant = variant;

        WritePlan plan = Plan(args);

        JObject symbol = JObject.Parse(plan.Details!["symbol"]);
        Assert.Equal(library, (string?)symbol["library"]);
        Assert.Equal(name, (string?)symbol["name"]);
        Assert.Equal(variant, (int?)symbol["variant"]);
        Assert.Equal("=GAA+A1-XG81:1", plan.Details["tag"]);
        Assert.Contains(plan.Changes, c => c.Kind == "terminal");
        Assert.Empty(_parts.Reads);
    }

    [Theory]
    [InlineData("Coil")]
    [InlineData("PLCTerminal")]
    public void TerminalSymbol_NonTerminalCategoryIsInvalidArgumentEvenWhenKindSaysTerminal(string? category)
    {
        SetSymbol("IEC_symbol", "X2_2", 0, "Function", "terminal", category);
        DevicePlaceArgs args = TerminalArgs();
        args.Library = "IEC_symbol";
        args.Symbol = "X2_2";
        args.SymbolVariant = 0;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void TerminalSymbol_SpecialDcUsesTerminalCategoryValidationWithoutBlackBoxCorners()
    {
        SetSymbol("SPECIAL", "DC", 0, "BoxFunction", "other", "Blackbox");
        DevicePlaceArgs args = TerminalArgs();
        args.Library = "SPECIAL";
        args.Symbol = "DC";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/symbol: terminal symbols", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/corner_x", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Terminal")]
    public void TerminalSymbol_SpecialDcpDoesNotBypassDuplicateTerminalNumber(string? category)
    {
        _names.Functions.Add(("=GAA+A1-XG81:1", "1/1/1/0", false, Page, "XG81:1"));
        SetSymbol("SPECIAL", "DCP", 0, "Function", "terminal", category);
        DevicePlaceArgs args = TerminalArgs();
        args.Library = "SPECIAL";
        args.Symbol = "DCP";

        Assert.Equal(ErrorReason.AlreadyExists, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void TerminalSymbol_UnreadableCategoryWarnsWithoutRefusing()
    {
        SetSymbol("IEC_symbol", "X2_2", 0, "Function", "terminal", null);
        DevicePlaceArgs args = TerminalArgs();
        args.Library = "IEC_symbol";
        args.Symbol = "X2_2";
        Assert.Contains("category is unavailable", Plan(args).Details!["layoutWarnings"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "X2_2")]
    [InlineData("IEC_symbol", null)]
    [InlineData("", "X2_2")]
    [InlineData("IEC_symbol", " ")]
    public void TerminalSymbol_IncompletePairIsInvalid(string? library, string? symbol)
    {
        DevicePlaceArgs args = TerminalArgs();
        args.Library = library;
        args.Symbol = symbol;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void TerminalSymbol_ExplicitSourceDefaultsToVariantZero()
    {
        SetSymbol("IEC_symbol", "X2_2", 0, "Function", "terminal", "Terminal");
        DevicePlaceArgs args = TerminalArgs();
        args.Library = "IEC_symbol";
        args.Symbol = "X2_2";
        Assert.Equal(0, (int?)JObject.Parse(Plan(args).Details!["symbol"])["variant"]);
    }

    [Fact]
    public void TerminalSymbol_OmittedSourceKeepsIecXVariantZero()
    {
        SetSymbol("IEC_symbol", "X", 0, "Function", "function", "Terminal");
        JObject symbol = JObject.Parse(Plan(TerminalArgs()).Details!["symbol"]);
        Assert.Equal("IEC_symbol", (string?)symbol["library"]);
        Assert.Equal("X", (string?)symbol["name"]);
        Assert.Equal(0, (int?)symbol["variant"]);
    }

    private static DevicePlaceArgs TerminalArgs() => new()
    { Project = "Line1", Page = Page, TerminalStrip = "-XG81", TerminalNumber = "1", X = 160, Y = 240 };
}
