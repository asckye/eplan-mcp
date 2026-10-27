using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class SymbolDtoTests
{
    [Theory]
    [InlineData("Routing", null, "routing")]
    [InlineData("DynamicRouting", null, "routing")]
    [InlineData("TNodeUp", null, "t_node")]
    [InlineData("TNodeDown", null, "t_node")]
    [InlineData("TNodeLeft", null, "t_node")]
    [InlineData("TNodeRight", null, "t_node")]
    [InlineData("PotentialTerminal", null, "potential_point")]
    [InlineData("PotentialDefinition", null, "potential_definition")]
    [InlineData("InterruptionPoint", null, "interruption_point")]
    [InlineData("LocationBox", null, "location_box")]
    [InlineData("CableDefinitionLine", null, "cable_definition")]
    [InlineData("Function", "PLCBox", "plc_box")]
    [InlineData("BoxFunction", "PLCBox", "plc_box")]
    [InlineData("Function", "Terminal", "terminal")]
    [InlineData("Function", "PLCTerminal", "function")]
    [InlineData("BoxFunction", null, "function")]
    [InlineData("Function", "Coil", "function")]
    [InlineData("StrandConnectorAngle", null, "other")]
    [InlineData("UnknownFutureType", "PLCBox", "other")]
    public void Kind_UsesNativeTypeAndOnlyRefinesFunctions(string type, string? category, string expected)
    {
        Assert.Equal(expected, SymbolKinds.From(type, category));
        Assert.Contains(expected, SymbolKinds.All);
    }

    [Theory]
    [InlineData("special", "co", "routing", true)]
    [InlineData("special", "co", "t_node", false)]
    [InlineData("spec", "co", "routing", false)]
    [InlineData(null, null, null, true)]
    public void Filters_CombineExactLibraryNameSubstringAndKind(string? library, string? name, string? kind, bool expected) =>
        Assert.Equal(expected, SymbolKinds.Matches(new SymbolSearchArgs { Library = library, NameContains = name, Kind = kind }, "SPECIAL", "CO", "routing"));

    [Fact]
    public void Variant_RoundTripsAcrossBothPipeSerializers()
    {
        var item = new SymbolSearchItem { Library = "SPECIAL", Name = "CO", Variant = 2, Type = "Routing", Kind = "routing", ConnectionPoints = [new() { Location = [-4, 0], Direction = "Left" }] };
        string json = System.Text.Json.JsonSerializer.Serialize(item, JsonSerializerOptions.Web);
        SymbolSearchItem read = JsonConvert.DeserializeObject<SymbolSearchItem>(json)!;
        Assert.Equal(("SPECIAL", "CO", 2, "Routing", "routing"), (read.Library, read.Name, read.Variant, read.Type, read.Kind));
        SymbolConnectionPoint point = Assert.Single(read.ConnectionPoints);
        Assert.Equal([-4d, 0d], point.Location);
        Assert.Equal("Left", point.Direction);
        Assert.Equal(item.Variant, System.Text.Json.JsonSerializer.Deserialize<SymbolSearchItem>(JsonConvert.SerializeObject(read))!.Variant);
    }
}
