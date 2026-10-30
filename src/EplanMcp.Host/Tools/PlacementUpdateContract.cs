using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>Full-profile moves use complete locators and closed absolute/delta branches.</summary>
internal static class PlacementUpdateContract
{
    internal static readonly JsonElement Input = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["project","page","moves"],"properties":{
          "project":{"type":"string","minLength":1},"page":{"type":"string","minLength":1},
          "moves":{"type":"array","minItems":1,"maxItems":50,"items":{"oneOf":[
            {"type":"object","additionalProperties":false,"required":["locator","x_mm","y_mm"],"properties":{"locator":{"$ref":"#/$defs/locator"},"x_mm":{"type":"number","minimum":0,"maximum":10000},"y_mm":{"type":"number","minimum":0,"maximum":10000}}},
            {"type":"object","additionalProperties":false,"required":["locator","delta_mm"],"properties":{"locator":{"$ref":"#/$defs/locator"},"delta_mm":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number","minimum":-10000,"maximum":10000}}}}
          ]}},"dry_run":{"type":"boolean","default":false},"show_page":{"type":"boolean","default":false,"description":"Open or focus this page in the EPLAN editor before drawing so the user can watch; UI only, no data change."},"snap_to_grid":{"type":"boolean","default":true},"response_format":{"enum":["concise","detailed"],"default":"concise"}},
          "$defs":{"locator":{"type":"object","additionalProperties":false,"required":["project","page","type","oid","digest"],"properties":{"project":{"type":"string","minLength":1},"page":{"type":"string","minLength":1},"type":{"type":"integer","minimum":1,"maximum":2147483647},"oid":{"type":"integer","minimum":1,"maximum":9223372036854775807},"digest":{"type":"string","pattern":"^[a-f0-9]{64}$"}}}}}
        """).RootElement.Clone();

    internal static Tool Definition => new()
    {
        Name = "eplan_placement_update",
        Title = "Move placements on an EPLAN page",
        Description = "Moves same-page symbols and text/line/rectangle from eplan_page_get include_drawings locators. mm, bottom left, y up; x_mm/y_mm or delta_mm. Property text follows its parent; groups refused. One undo; no-op leaves none. Approval outside scratch; denial returns APPROVAL_DENIED. Use eplan_connection_generate afterwards.",
        InputSchema = Input,
        OutputSchema = Output(),
        Annotations = new() { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false },
    };

    internal static void Configure(IEnumerable<ModelContextProtocol.Server.McpServerTool> tools)
    {
        Tool tool = tools.Single(t => t.ProtocolTool.Name == "eplan_placement_update").ProtocolTool;
        tool.Title = Definition.Title;
        tool.Description = Definition.Description;
        tool.InputSchema = Input;
        tool.OutputSchema = Definition.OutputSchema;
    }

    private static JsonElement Output()
    {
        JsonNode schema = JsonNode.Parse("""
            {"type":"object","additionalProperties":false,"required":["project","page","execution","dryRun","noOp","gridSize","boundarySource","moves","fingerprint","graphicalChanges","connectionChanges","generation"],"properties":{
              "project":{"type":"string"},"page":{"type":"string"},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},"noOp":{"type":"boolean"},"gridSize":{"type":"number","exclusiveMinimum":0},"boundarySource":{"const":"Page.Size"},"fingerprint":{"type":"string"},"generation":{"type":"string"},"pageShown":{"type":["boolean","null"]},"hint":{"type":["string","null"]},"warning":{"type":["string","null"]},"recovered":{"type":"boolean"},
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
