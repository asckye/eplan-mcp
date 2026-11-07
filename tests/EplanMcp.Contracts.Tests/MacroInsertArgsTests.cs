using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class MacroInsertArgsTests
{
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"values\":{\"#<EES_F_CH1>\":\"6A\"}}", true)]
    public void Values_SurviveBothPipeSerializersWithExactNames(string json, bool hasValues)
    {
        MacroInsertArgs bridge = JsonConvert.DeserializeObject<MacroInsertArgs>(json)!;
        MacroInsertArgs host = System.Text.Json.JsonSerializer.Deserialize<MacroInsertArgs>(json, JsonSerializerOptions.Web)!;
        MacroInsertArgs[] copies =
        [
            bridge,
            host,
            JsonConvert.DeserializeObject<MacroInsertArgs>(System.Text.Json.JsonSerializer.Serialize(host, JsonSerializerOptions.Web))!,
            System.Text.Json.JsonSerializer.Deserialize<MacroInsertArgs>(JsonConvert.SerializeObject(bridge), JsonSerializerOptions.Web)!,
        ];

        foreach (MacroInsertArgs copy in copies)
        {
            if (hasValues)
            {
                KeyValuePair<string, string> value = Assert.Single(copy.Values!);
                Assert.Equal(("#<EES_F_CH1>", "6A"), (value.Key, value.Value));
            }
            else
            {
                Assert.Null(copy.Values);
            }
        }
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"macroBox\":true}", true)]
    [InlineData("{\"macroBox\":false}", false)]
    public void MacroBox_DefaultAndExplicitValuesSurviveBothPipeSerializers(string json, bool expected)
    {
        MacroInsertArgs bridge = JsonConvert.DeserializeObject<MacroInsertArgs>(json)!;
        MacroInsertArgs host = System.Text.Json.JsonSerializer.Deserialize<MacroInsertArgs>(json, JsonSerializerOptions.Web)!;
        Assert.Equal(expected, bridge.MacroBox);
        Assert.Equal(expected, host.MacroBox);
        Assert.Equal(expected, JsonConvert.DeserializeObject<MacroInsertArgs>(System.Text.Json.JsonSerializer.Serialize(host, JsonSerializerOptions.Web))!.MacroBox);
        Assert.Equal(expected, System.Text.Json.JsonSerializer.Deserialize<MacroInsertArgs>(JsonConvert.SerializeObject(bridge), JsonSerializerOptions.Web)!.MacroBox);
    }
}
