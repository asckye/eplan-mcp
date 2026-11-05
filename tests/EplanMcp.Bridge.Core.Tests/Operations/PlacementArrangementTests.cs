using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementArrangementTests
{
    [Theory]
    [InlineData("左,0°", true)]
    [InlineData("不存在", false)]
    [InlineData("左,0° ", false)]
    public void LiveFailingBpRequest_MatchesExactLocalizedArrangement(string name, bool accepted)
    {
        var fake = PlacementSymbolEditTests.Symbol("BP");
        fake.Data["scene"]!["page"]!["page"] = "=K1+A2&EFS1/2";
        fake.Data["objects"]![0]!["locator"]!["page"] = "=K1+A2&EFS1/2";
        fake.Data["objects"]![0]!["properties"]!["name"] = "=GAB1-24V";
        fake.Data["objects"]![0]!["arrangement"] = "默认 (右,0°)";
        fake.Data["objects"]![0]!["arrangements"] = new JArray("左,0°", "默认 (右,0°)");
        fake.Data["objects"]![0]!["activePropertySet"] = "默认 (右,0°)";
        fake.Data["objects"]![0]!["children"] = new JArray(new JObject { ["propertyId"] = 20010, ["x"] = 3, ["alignment"] = "MiddleLeft" });
        JObject request = fake.Input("set_arrangement");
        request["page"] = "=K1+A2&EFS1/2";
        request["property_arrangement"] = name;
        var plan = new PlacementEditOperation(fake, fake, false, fake.Backup);
        if (!accepted)
        {
            var error = Assert.Throws<BridgeErrorException>(() => plan.Execute(request));
            Assert.Contains("左,0°", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, fake.Transactions);
            return;
        }
        JObject preview = (JObject)plan.Execute(request)!;
        fake.Mutation = () => { fake.Data["objects"]![0]!["arrangement"] = name; fake.Data["objects"]![0]!["activePropertySet"] = name; };
        JObject result = (JObject)new PlacementEditExecuteOperation(fake, fake, fake.Backup).Execute(new JObject { ["request"] = request, ["fingerprint"] = preview["fingerprint"] })!;
        Assert.Equal(name, (string?)result["predicted"]![0]!["arrangement"]);
        Assert.Equal(name, (string?)result["predicted"]![0]!["activePropertySet"]);
        Assert.Equal(0, (int)result["predicted"]![0]!["symbol"]!["variant"]!);
        Assert.Equal(1, fake.Closed);
    }
    [Fact]
    public void NativeArrangement_UsesExactNameAndReusesSuppliedPropertyWrapper()
    {
#pragma warning disable CA2000 // Borrowed native fixture handles must not be disposed.
        var symbol = new Function();
#pragma warning restore CA2000
        symbol.PropertyPlacementsSchemas.All = [new("左,0°"), new("默认 (右,0°)")];
        symbol.PropertyPlacementsSchemas.OnSelect = schema => symbol.Properties[(Properties.Function)19307] = schema.Name;
        EplanSymbolArrangement.Select(symbol, "左,0°");
        using FunctionPropertyList properties = symbol.Properties;
        Assert.Equal("左,0°", EplanSymbolArrangement.SelectedName(symbol));
        Assert.Equal("左,0°", EplanSymbolArrangement.ReadActive(properties));
        Assert.False(properties.Disposed);
        Assert.Throws<BridgeErrorException>(() => EplanSymbolArrangement.Select(symbol, "Left"));
    }
    [Fact]
    public void UnreadableActivePropertySet_WarnsButDoesNotReject()
    {
        var fake = PlacementSymbolEditTests.Symbol("BP");
        fake.Data["objects"]![0]!["arrangement"] = "right";
        fake.Data["objects"]![0]!["arrangements"] = new JArray("left", "right");
        JObject request = fake.Input("set_arrangement");
        request["property_arrangement"] = "left";
        JObject plan = (JObject)new PlacementEditOperation(fake, fake, false, fake.Backup).Execute(request)!;
        fake.Mutation = () => fake.Data["objects"]![0]!["arrangement"] = "left";
        JObject result = (JObject)new PlacementEditExecuteOperation(fake, fake, fake.Backup).Execute(new JObject { ["request"] = request, ["fingerprint"] = plan["fingerprint"] })!;
        Assert.Contains("19307", (string)result["warning"]!, StringComparison.Ordinal);
        Assert.Equal(1, fake.Closed);
    }
}
