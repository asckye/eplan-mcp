using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Tests;

internal static class WorkflowFixtures
{
    public static JsonObject Spec() => JsonNode.Parse("""
        {"parameters":{
          "mode":{"type":"choice","enum":["dol","star_delta"],"required":true,"default":"dol"},
          "label":{"type":"text","required":false,"default":"Motor"},
          "x":{"type":"number","unit":"mm","min":0,"max":100,"required":false,"default":40}
        },"steps":[
          {"op":"page.create","args":{"pages":[{"name":"=AP+A1/1","description":{"parameter":"label"}}]}},
          {"op":"macro.insert","args":{"page":"=AP+A1/1","macro":"macro/motor.ema","x":{"parameter":"x"},"y":200},"dependsOn":[1]}
        ]}
        """)!.AsObject();

    public static JsonElement ConfigSchema => JsonSerializer.SerializeToElement(JsonNode.Parse("""
        {"items":{
          "motor.starter":{"type":"choice","enum":["dol","star_delta"],"required":true,"allowedLayers":["base"]},
          "motor.power":{"type":"number","unit":"kW","min":0,"required":false,"allowedLayers":["base"]},
          "motor.enabled":{"type":"boolean","required":false,"allowedLayers":["base"]}
        }}
        """));

    public static JsonElement Element(string json) => JsonSerializer.SerializeToElement(JsonNode.Parse(json));

    public static WorkflowDefinition Validate(JsonObject? spec = null) => WorkflowValidator.Validate(JsonSerializer.SerializeToElement(spec ?? Spec()), ConfigSchema,
        (name, kind) => kind == ResourceKind.Macro && name == "macro/motor.ema");

    public static JsonObject BranchSpec()
    {
        JsonObject spec = Spec();
        spec["branches"] = JsonNode.Parse("""{"starter":{"parameter":"mode"}}""");
        var steps = spec["steps"]!.AsArray();
        steps[1]!["branch"] = "starter";
        steps[1]!["case"] = "dol";
        JsonNode alternate = steps[1]!.DeepClone();
        alternate["case"] = "star_delta";
        alternate["args"]!["y"] = 250;
        steps.Add(alternate);
        steps.Add(JsonNode.Parse("""{"op":"page.update","branch":"starter","case":"star_delta","args":{"pages":[{"name":"=AP+A1/1","description":"Star delta"}]},"dependsOn":[1,3]}"""));
        return spec;
    }
}
