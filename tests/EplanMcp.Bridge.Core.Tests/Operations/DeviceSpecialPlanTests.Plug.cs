using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DeviceSpecialPlanTests
{
    [Theory]
    [InlineData("PDEF", "TextFunction", "other", "ConnectorDefText", 0)]
    public void PlugSymbols_ReportedPdefSourceAndXbsPreviewWithNativeCategory(string name, string type, string kind, string category, int count)
    {
        // The reported failing source is SPECIAL/PDEF; remaining inputs use this fixture's valid page and tag.
        SetSymbol("SPECIAL", name, 0, type, kind, category);
        _source.Symbol.ConnectionPoints = [.. Enumerable.Range(0, count).Select(i => new SymbolConnectionPoint { Location = [0, i * 4] })];
        DevicePlaceArgs args = SymbolArgs(name, "-X9");
        if (count > 0)
        { args.ConnectionDesignations = ["1", "2"]; }

        WritePlan plan = Plan(args);

        Assert.Equal(category, (string?)JObject.Parse(plan.Details!["symbol"])["functionCategory"]);
        Assert.Equal("=GAA+A1-X9", plan.Details["tag"]);
        Assert.Equal(count + 1, plan.Changes.Count);
        Assert.Empty(_parts.Reads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PlugDefinition_RequiresOmittedConnectionDesignations(int count)
    {
        SetSymbol("SPECIAL", "PDEF", 0, "TextFunction", "other", "ConnectorDefText");
        DevicePlaceArgs args = SymbolArgs("PDEF", "-X9");
        args.ConnectionDesignations = [.. Enumerable.Repeat("1", count)];
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/connection_designations: omit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PDEF")]
    public void PlugSymbols_UnreadableMetadataWarns(string name)
    {
        SetSymbol("SPECIAL", name, 0, "", "other", null);
        Assert.Contains("Plug symbol metadata is incomplete", Plan(SymbolArgs(name, "-X9")).Details!["layoutWarnings"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SH", "Function", "other")]
    [InlineData("OTHER", "TextFunction", "other")]
    public void OtherSpecialSymbols_RetainCapabilityRefusal(string name, string type, string kind)
    {
        SetSymbol("SPECIAL", name, 0, type, kind, null);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(SymbolArgs(name, "-X9"))).Error;
        Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
        Assert.Equal("device.place.symbol.function", error.Metadata!["missing"]);
    }

    [Theory]
    [InlineData("PDEF", "Function", "ConnectorDefText")]
    [InlineData("PDEF", "TextFunction", "Coil")]
    public void PlugSymbols_RejectKnownWrongNativeTypeOrCategory(string name, string type, string category)
    {
        SetSymbol("SPECIAL", name, 0, type, "other", category);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(SymbolArgs(name, "-X9"))).Error.Reason);
    }
}
