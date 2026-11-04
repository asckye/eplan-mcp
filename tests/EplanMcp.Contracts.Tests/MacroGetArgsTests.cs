using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class MacroGetArgsTests
{
    [Theory]
    [InlineData("{}", null, 0)]
    [InlineData("{\"representation\":\"Overview\",\"variant\":0}", "Overview", 0)]
    [InlineData("{\"representation\":\"MultiLine\",\"variant\":1}", "MultiLine", 1)]
    public void Selection_DefaultAndExplicitValuesSurviveBothPipeSerializers(string json, string? representation, int variant)
    {
        MacroGetArgs bridge = JsonConvert.DeserializeObject<MacroGetArgs>(json)!;
        MacroGetArgs host = System.Text.Json.JsonSerializer.Deserialize<MacroGetArgs>(json, JsonSerializerOptions.Web)!;
        Assert.Equal((representation, variant), (bridge.Representation, bridge.Variant));
        Assert.Equal((representation, variant), (host.Representation, host.Variant));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PositionAndNullSurviveBothPipeSerializers(bool available)
    {
        var data = new MacroGetData
        {
            ReferencePoint = available ? [0, 292] : null,
            Devices = [new MacroDevice { Tag = "+A1-A1", Location = available ? [44, 132] : null }]
        };
        MacroGetData bridge = JsonConvert.DeserializeObject<MacroGetData>(System.Text.Json.JsonSerializer.Serialize(data, JsonSerializerOptions.Web))!;
        MacroGetData host = System.Text.Json.JsonSerializer.Deserialize<MacroGetData>(JsonConvert.SerializeObject(data), JsonSerializerOptions.Web)!;
        Assert.Equal(data.ReferencePoint, bridge.ReferencePoint);
        Assert.Equal(data.ReferencePoint, host.ReferencePoint);
        Assert.Equal(data.Devices[0].Location, bridge.Devices[0].Location);
        Assert.Equal(data.Devices[0].Location, host.Devices[0].Location);
    }
}
