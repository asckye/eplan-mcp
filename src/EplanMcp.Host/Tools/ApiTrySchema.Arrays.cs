using System.Text.Json.Nodes;
namespace EplanMcp.Host.Tools;

internal static partial class ApiTrySchema
{
    private static void ConfigureArrays(JsonNode input, JsonNode output)
    {
        var atoms = (JsonArray)input["properties"]!["parameters"]!["additionalProperties"]!["oneOf"]!;
        JsonArray elements = new(atoms.Select(n => n!.DeepClone()).ToArray());
        atoms.Add(new JsonObject { ["type"] = "array", ["maxItems"] = 200, ["items"] = new JsonObject { ["oneOf"] = elements } });
        input["properties"]!["result_cursor"] = JsonNode.Parse("""{"type":["string","null"],"maxLength":1024,"description":"Read cache; same group/project/parameters; no API call."}""");
        var values = (JsonArray)output["properties"]!["value"]!["oneOf"]!;
        values.Add(elements[3]!.DeepClone());
        JsonArray returned = new(elements.Select(n => n!.DeepClone()).Append(JsonNode.Parse("""{"type":"null"}""")).ToArray());
        JsonNode page = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["items","total","nextCursor","truncated","hint"],"properties":{"items":{"type":"array","maxItems":200},"total":{"type":"integer"},"nextCursor":{"type":["string","null"]},"truncated":{"type":"boolean"},"hint":{"type":["string","null"]}}}""")!;
        page["properties"]!["items"]!["items"] = new JsonObject { ["oneOf"] = returned.DeepClone() };
        values.Add(page.DeepClone());
        returned.Add(page);
        values.Add(new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("returnValue", "outputs"),
            ["properties"] = new JsonObject
            {
                ["returnValue"] = new JsonObject { ["oneOf"] = returned.DeepClone() },
                ["outputs"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["oneOf"] = returned } },
            }
        });
    }
}
