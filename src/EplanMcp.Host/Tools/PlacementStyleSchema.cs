using System.Text.Json.Nodes;
namespace EplanMcp.Host.Tools;

internal static class PlacementStyleSchema
{
    internal static readonly string[] Alignments = ["FromLayer", "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight", "BaseLineLeft", "BaseLineCenter", "BaseLineRight", "SpecialLeft", "SpecialCenter", "SpecialRight", "JicSpecialLeft", "JicSpecialCenter", "JicSpecialRight"];
    internal static void Add(JsonNode properties)
    {
        properties["layer"] = JsonNode.Parse("""{"type":"integer","minimum":0}""");
        properties["pen"] = JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{"color":{"type":"integer"},"width_mm":{"type":"number"},"style":{"type":"integer"},"pattern_length":{"type":"number"},"line_end":{"type":"integer"}}}""");
        foreach (var field in new[] { (Name: "color", Max: 255), (Name: "style", Max: 6), (Name: "line_end", Max: 2) })
        { properties["pen"]!["properties"]![field.Name] = field.Name == "line_end" ? JsonNode.Parse("{\"type\":\"integer\",\"minimum\":0,\"maximum\":" + field.Max + "}") : JsonNode.Parse("{\"oneOf\":[{\"const\":-16002},{\"type\":\"integer\",\"minimum\":0,\"maximum\":" + field.Max + "}]}"); }
    }
    internal static JsonNode Alignment() => new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Alignments.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) };
}
