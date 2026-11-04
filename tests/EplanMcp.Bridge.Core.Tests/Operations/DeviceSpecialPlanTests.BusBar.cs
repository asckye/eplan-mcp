using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DeviceSpecialPlanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BusBarDefinition_PreviewResolvesTextFunctionAndMayJoinExistingPoints(bool existingPoint)
    {
        if (existingPoint)
        { _names.Functions.Add(("=GAA+A1-WE9", "1/1/1/0", true, Page, "WE9")); }
        SetSymbol("SPECIAL", "BBDEF", 0, "TextFunction", "other", "BusBarDefText");

        WritePlan plan = Plan(SymbolArgs("BBDEF", "-WE9"));

        Assert.Equal("=GAA+A1-WE9", plan.Details!["tag"]);
        JObject symbol = JObject.Parse(plan.Details["symbol"]);
        Assert.Equal("TextFunction", (string?)symbol["type"]);
        Assert.Equal("BusBarDefText", (string?)symbol["functionCategory"]);
        Assert.Contains("SPECIAL/BBDEF", Assert.Single(plan.Changes).NewValue, StringComparison.Ordinal);
        Assert.Empty(_parts.Reads);
    }

    [Theory]
    [InlineData("Function", "BusBarDefText")]
    [InlineData("TextFunction", "Coil")]
    public void BusBarDefinition_RequiresDiscoveredTextFunctionCategory(string type, string category)
    {
        SetSymbol("SPECIAL", "BBDEF", 0, type, "other", category);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(SymbolArgs("BBDEF", "-WE9"))).Error.Reason);
    }

    [Theory]
    [InlineData("", "BusBarDefText")]
    [InlineData("TextFunction", null)]
    [InlineData("", null)]
    public void BusBarDefinition_MissingMetadataWarnsAndStillPreviews(string type, string? category)
    {
        SetSymbol("SPECIAL", "BBDEF", 0, type, "other", category);
        WritePlan plan = Plan(SymbolArgs("BBDEF", "-WE9"));
        Assert.Contains("Busbar definition metadata is incomplete", plan.Details!["layoutWarnings"], StringComparison.Ordinal);
        Assert.Contains("SPECIAL/BBDEF", Assert.Single(plan.Changes).NewValue, StringComparison.Ordinal);
        Assert.Empty(_parts.Reads);
    }

    [Fact]
    public void BusBarPoint_PreviewCanJoinExistingDefinition()
    {
        _names.Functions.Add(("=GAA+A1-WE9", "1/1/1/0", true, Page, "WE9"));
        SetSymbol("SPECIAL", "BCP", 0, "Function", "function", "Busbar");
        Assert.Equal("=GAA+A1-WE9", Plan(SymbolArgs("BCP", "-WE9")).Details!["tag"]);
    }

    [Fact]
    public void BusBarDefinition_BridgeRequiresTag()
    {
        SetSymbol("SPECIAL", "BBDEF", 0, "TextFunction", "other", "BusBarDefText");
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(SymbolArgs("BBDEF", null))).Error.Reason);
    }
}
