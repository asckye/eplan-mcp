using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class MacroInsertArgsTests
{
    [Fact]
    public void Items_ParametricDriveSurvivesBothPipeSerializers()
    {
        const string json = """
            {"project":"EES1","items":[
              {"page":"=MA1+UC1&EFS1/1","macro":"EPLAN_Macro/203_Electrical_Engine/101_01_Variant_1/Reverse_Contactor_Control.ema","x":4,"y":288},
              {"page":"=MA1+UC1&EFS1/2","macro":"EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema","x":24,"y":276,"values":{"#<Function_text>":"Forward control"}},
              {"page":"=MA1+UC1&EFS1/2","macro":"EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema","x":76,"y":276,"values":{"#<Function_text>":"Reverse control"}}
            ]}
            """;
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
            Assert.Null(copy.Page);
            Assert.Null(copy.MacroBox);
            Assert.Null(copy.X);
            Assert.Null(copy.Y);
            Assert.Null(copy.Variant);
            Assert.Equal("EES1", copy.Project);
            Assert.Equal(3, copy.Items!.Count);
            Assert.Equal("=MA1+UC1&EFS1/1", copy.Items[0].Page);
            Assert.Equal((4, 288), (copy.Items[0].X, copy.Items[0].Y));
            Assert.True(copy.Items[0].MacroBox);
            Assert.Equal("Forward control", copy.Items[1].Values!["#<Function_text>"]);
            Assert.Equal("Reverse control", copy.Items[2].Values!["#<Function_text>"]);
            Assert.Equal(76, copy.Items[2].X);
        }
    }

    [Theory]
    [InlineData("{\"macroBox\":true,\"items\":[]}")]
    [InlineData("{\"items\":[],\"macroBox\":true}")]
    public void Items_ExplicitTopLevelMacroBoxSurvivesPropertyOrder(string json)
    {
        Assert.True(JsonConvert.DeserializeObject<MacroInsertArgs>(json)!.MacroBox);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<MacroInsertArgs>(json, JsonSerializerOptions.Web)!.MacroBox);
    }

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
