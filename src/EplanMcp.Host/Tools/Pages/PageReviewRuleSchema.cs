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
