using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>T5a contract only; registration and execution require T5b.</summary>
internal static class PlacementUpdateContract
{
    internal static readonly JsonElement Input = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["project","page","moves"],"properties":{
          "project":{"type":"string","minLength":1},"page":{"type":"string","minLength":1},
          "moves":{"type":"array","minItems":1,"maxItems":50,"items":{"oneOf":[
            {"type":"object","additionalProperties":false,"required":["locator","x_mm","y_mm"],"properties":{"locator":{"$ref":"#/$defs/locator"},"x_mm":{"type":"number","minimum":0,"maximum":10000},"y_mm":{"type":"number","minimum":0,"maximum":10000}}},
            {"type":"object","additionalProperties":false,"required":["locator","delta_mm"],"properties":{"locator":{"$ref":"#/$defs/locator"},"delta_mm":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number","minimum":-10000,"maximum":10000}}}}
          ]}},"dry_run":{"type":"boolean","default":false},"snap_to_grid":{"type":"boolean","default":true},"response_format":{"enum":["concise","detailed"],"default":"concise"}},
          "$defs":{"locator":{"type":"object","additionalProperties":false,"required":["project","page","type","oid","digest"],"properties":{"project":{"type":"string","minLength":1},"page":{"type":"string","minLength":1},"type":{"type":"integer","minimum":1,"maximum":2147483647},"oid":{"type":"integer","minimum":1,"maximum":9223372036854775807},"digest":{"type":"string","pattern":"^[a-f0-9]{64}$"}}}}}
        """).RootElement.Clone();

    internal static Tool Definition => new()
    {
        Name = "eplan_placement_update",
        Title = "Plan absolute placement moves on an EPLAN page",
        Description = "Plans same-page moves from eplan_page_get include_drawings locators. Absolute x_mm/y_mm or delta_mm; deltas resolve to absolute positions. Property text follows its parent. Groups are unsupported. Dry run only until T5b; zero delta is a no-op.",
        InputSchema = Input,
        OutputSchema = Output(),
        Annotations = new() { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false },
    };

    private static JsonElement Output()
    {
        JsonNode schema = JsonNode.Parse("""
            {"type":"object","additionalProperties":false,"required":["project","page","execution","dryRun","noOp","gridSize","boundarySource","moves","fingerprint","graphicalChanges","connectionChanges","generation"],"properties":{
              "project":{"type":"string"},"page":{"type":"string"},"execution":{"const":"not_started"},"dryRun":{"const":true},"noOp":{"type":"boolean"},"gridSize":{"type":"number","exclusiveMinimum":0},"boundarySource":{"const":"Page.Size"},"fingerprint":{"type":"string"},"generation":{"type":"string"},
              "moves":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["locator","parent","before","requested","after","snapped","bounds"],"properties":{"locator":{"$ref":"#/$defs/locator"},"parent":{"oneOf":[{"type":"null"},{"$ref":"#/$defs/locator"}]},"before":{"$ref":"#/$defs/point"},"requested":{"$ref":"#/$defs/point"},"after":{"$ref":"#/$defs/point"},"snapped":{"type":"boolean"},"bounds":{"type":"array","minItems":4,"maxItems":4,"items":{"type":"number"}}}}},
              "graphicalChanges":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["pins","before","after"],"properties":{"pins":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"string"}},"before":{"$ref":"#/$defs/segment"},"after":{"$ref":"#/$defs/segment"}}}},
              "connectionChanges":{"type":"array","items":{"type":"array","minItems":2,"maxItems":2,"items":{"type":["string","null"]}}}}}
            """)!;
        schema["$defs"] = JsonNode.Parse(Input.GetProperty("$defs").GetRawText());
        schema["$defs"]!["point"] = JsonNode.Parse("""{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number"}}""");
        schema["$defs"]!["segment"] = JsonNode.Parse("""{"type":["array","null"],"minItems":2,"maxItems":2,"items":{"$ref":"#/$defs/point"}}""");
        return JsonSerializer.SerializeToElement(schema);
    }
}
