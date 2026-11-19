using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

internal static class PageReviewRuleSchema
{
    internal static void Configure(Tool tool)
    {
        JsonNode output = JsonNode.Parse(tool.OutputSchema!.Value.GetRawText())!;
        JsonNode properties = output["properties"]!;
        output["required"]!.AsArray().Add("layout");
        properties["layout"] = JsonNode.Parse("""
            {
              "type":"object","additionalProperties":false,
              "required":["parallelSpacingMm","minimumTextHeightMm","marginMm","sources"],
              "properties":{
                "parallelSpacingMm":{"type":["number","null"]},
                "minimumTextHeightMm":{"type":"number"},
                "marginMm":{"type":"number"},
                "sources":{
                  "type":"object","additionalProperties":false,
                  "required":["parallelSpacingMm","minimumTextHeightMm","marginMm"],
                  "properties":{
                    "parallelSpacingMm":{"$ref":"#/$defs/layoutSource"},
                    "minimumTextHeightMm":{"$ref":"#/$defs/layoutSource"},
                    "marginMm":{"$ref":"#/$defs/layoutSource"}
                  }
                }
              }
            }
            """);
        output["$defs"]!["layoutSource"] = JsonNode.Parse("""{"type":"string","pattern":"^(config|default|plugin:[a-z0-9]+(?:[.-][a-z0-9]+)*)$"}""");
        JsonNode issue = properties["issues"]!["items"]!["properties"]!;
        issue["code"] = new JsonObject { ["anyOf"] = new JsonArray(issue["code"]!.DeepClone(), JsonNode.Parse("""{"type":"string","pattern":"^rule(?:_unknown)?:[^/]+/[^/]+$"}""")) };
        issue["reference"] = new JsonObject();
        issue["objects"]!["items"] = new JsonObject
        {
            ["anyOf"] = new JsonArray(issue["objects"]!["items"]!.DeepClone(), JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","page"],"properties":{"project":{"type":"string"},"page":{"type":"string"}}}""")),
        };
        properties["rulePacks"] = JsonNode.Parse("""{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","version","status","standards","rules","passed","failed","unknown","notApplicable"],"properties":{"id":{"type":"string"},"version":{"type":"string"},"status":{"enum":["draft","signed"]},"standards":{"type":"array","items":{"type":"string"}},"rules":{"type":"integer","minimum":0},"passed":{"type":"integer","minimum":0},"failed":{"type":"integer","minimum":0},"unknown":{"type":"integer","minimum":0},"notApplicable":{"type":"integer","minimum":0}}}}""");
        properties["metadata"] = JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{"plugins":{"type":"array","items":{"type":"string"}},"warnings":{"type":"array","items":{"type":"string"}}}}""");
        tool.OutputSchema = JsonSerializer.SerializeToElement(output);
    }
}
