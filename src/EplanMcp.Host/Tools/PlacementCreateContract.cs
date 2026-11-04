using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>Full-profile creation contract; raw validation enforces the closed item branches.</summary>
internal static class PlacementCreateContract
{
    internal static readonly JsonElement Input = Styled(JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["project","page","items"],"properties":{
          "project":{"type":"string","minLength":1},"page":{"type":"string","minLength":1},
          "items":{"type":"array","minItems":1,"maxItems":50,"items":{"$ref":"#/$defs/item"}},
          "snap_to_grid":{"type":"boolean","default":true},"dry_run":{"type":"boolean","default":true,"description":"Omit/true previews; false executes."},"show_page":{"type":"boolean","default":false,"description":"Open page (UI)."},
          "response_format":{"type":"string","enum":["concise","detailed"],"default":"concise"}},"$defs":{
          "point":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number","minimum":0,"maximum":10000}},
          "points":{"type":"array","minItems":2,"items":{"$ref":"#/$defs/point"}},
          "pair":{"type":"array","minItems":2,"maxItems":2,"items":{"$ref":"#/$defs/point"}},
          "item":{"oneOf":[
            {"type":"object","additionalProperties":false,"required":["kind","position","contents","height_mm"],"properties":{
              "kind":{"const":"text"},"position":{"$ref":"#/$defs/point"},"height_mm":{"type":"number","minimum":0.5,"maximum":100},
              "contents":{"type":"object","minProperties":1,"propertyNames":{"minLength":5,"maxLength":5,"pattern":"^([a-z]{2}_[A-Z]{2}|\\?\\?_\\?\\?)$"},"additionalProperties":{"type":"string","minLength":1,"maxLength":500}}}},
            {"type":"object","additionalProperties":false,"required":["kind","position","contents"],"properties":{
              "kind":{"const":"path_text"},"position":{"$ref":"#/$defs/point"},"height_mm":{"type":"number","minimum":0.5,"maximum":100},
              "contents":{"type":"object","minProperties":1,"propertyNames":{"minLength":5,"maxLength":5,"pattern":"^([a-z]{2}_[A-Z]{2}|\\?\\?_\\?\\?)$"},"additionalProperties":{"type":"string","minLength":1,"maxLength":500}}}},
            {"type":"object","additionalProperties":false,"required":["kind","points"],"properties":{"kind":{"const":"line"},"points":{"$ref":"#/$defs/pair"}}},
            {"type":"object","additionalProperties":false,"required":["kind","corners"],"properties":{"kind":{"const":"rectangle"},"corners":{"$ref":"#/$defs/pair"}}},
            {"type":"object","additionalProperties":false,"required":["kind","points"],"properties":{"kind":{"const":"polyline"},"points":{"$ref":"#/$defs/points"}}},
            {"type":"object","additionalProperties":false,"required":["kind","center","radius_mm","start_angle","end_angle"],"properties":{"kind":{"const":"arc"},"center":{"$ref":"#/$defs/point"},"radius_mm":{"type":"number","exclusiveMinimum":0,"maximum":10000},"start_angle":{"type":"number"},"end_angle":{"type":"number"}}},
            {"type":"object","additionalProperties":false,"required":["kind","center","radius_x_mm","radius_y_mm"],"properties":{"kind":{"const":"ellipse"},"center":{"$ref":"#/$defs/point"},"radius_x_mm":{"type":"number","exclusiveMinimum":0,"maximum":10000},"radius_y_mm":{"type":"number","exclusiveMinimum":0,"maximum":10000},"angle":{"type":"number","default":0}}},
            {"type":"object","additionalProperties":false,"required":["kind","file","corners"],"properties":{"kind":{"const":"image"},"file":{"type":"string","minLength":1},"corners":{"$ref":"#/$defs/pair"}}}
          ]}}}
        """).RootElement.Clone());

    internal static readonly JsonElement Output = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["project","page","execution","dryRun","gridSize","boundarySource","items","changes","connectionChanges"],"properties":{
          "project":{"type":"string"},"page":{"type":"string"},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},
          "gridSize":{"type":"number","exclusiveMinimum":0},"boundarySource":{"enum":["Page.Size","unverified"]},
          "items":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"object","additionalProperties":false,"required":["requested","actual","bounds","snapped"],"properties":{
            "requested":{"$ref":"#/$defs/item"},"actual":{"$ref":"#/$defs/item"},"bounds":{"type":["array","null"],"minItems":4,"maxItems":4,"items":{"type":"number"}},"snapped":{"type":"boolean"}}}},
          "changes":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"string"}},"connectionChanges":{"type":"array","maxItems":0},"fingerprint":{"type":"string"},"pageShown":{"type":["boolean","null"]},"hint":{"type":["string","null"]},"warning":{"type":["string","null"]},"recovered":{"type":"boolean"}}}
        """).RootElement.Clone();

    internal static Tool Definition => new()
    {
        Name = "eplan_placement_create",
        Title = "Create text and graphics on an EPLAN page",
        Description = "Creates text, line, rectangle, polyline, arc, ellipse, path_text or image on a Circuit, Overview or Graphics page; mm, bottom left, y up; angles in degrees. Path_text is native path function text for device function descriptions and reports. Image file: relative imports path. Omit dry_run to preview; false executes. One undo; approval outside scratch. " +
            "Text needs the project display language or ??_??; ??_?? cannot be mixed with languages (A54). " +
            "Example: {\"project\":\"P\",\"page\":\"1\",\"items\":[{\"kind\":\"line\",\"points\":[[1,1],[2,2]]}]}.",
        InputSchema = Input,
        OutputSchema = WithDefinitions(),
        Annotations = new() { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false },
    };

    internal static void Configure(IEnumerable<ModelContextProtocol.Server.McpServerTool> tools)
    {
        Tool tool = tools.Single(t => t.ProtocolTool.Name == PlacementCreateTools.Tool).ProtocolTool;
        tool.Title = Definition.Title;
        tool.Description = Definition.Description;
        tool.InputSchema = JsonDocument.Parse("""
            {"type":"object","additionalProperties":false,"required":["project","page","items"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"items":{"type":"array","items":{"additionalProperties":false,"required":["kind"],"properties":{"kind":{"enum":["text","line","rectangle","path_text","polyline","arc","ellipse","image"]},"position":{"type":"array"},"points":{"type":"array"},"corners":{"type":"array"},"center":{"type":"array"},"contents":{},"height_mm":{},"radius_mm":{},"radius_x_mm":{},"radius_y_mm":{},"angle":{},"start_angle":{},"end_angle":{},"file":{"type":"string"},"pen":{"type":"object"},"layer":{"type":"integer"},"closed":{"type":"boolean"},"alignment":{"type":"string"}},"type":"object"},"minItems":1,"maxItems":50},"snap_to_grid":{"type":"boolean","default":true},"dry_run":{"type":"boolean","default":true},"show_page":{"type":"boolean","default":false,"description":"Open page (UI)."},"response_format":{"enum":["concise","detailed"],"default":"concise"}}}
            """).RootElement.Clone();
        tool.OutputSchema = Definition.OutputSchema;
    }

    private static JsonElement Styled(JsonElement input)
    {
        JsonNode schema = JsonNode.Parse(input.GetRawText())!;
        foreach (JsonNode? branch in schema["$defs"]!["item"]!["oneOf"]!.AsArray())
        {
            JsonNode properties = branch!["properties"]!;
            PlacementStyleSchema.Add(properties);
            string kind = properties["kind"]!["const"]!.GetValue<string>();
            if (kind is "text" or "path_text" or "rectangle")
            { properties["angle"] = JsonNode.Parse("""{"type":"number"}"""); }
            if (kind is "text" or "path_text")
            { properties["alignment"] = PlacementStyleSchema.Alignment(); }
            if (kind == "polyline")
            { properties["closed"] = JsonNode.Parse("""{"type":"boolean","default":false}"""); }
        }
        return JsonSerializer.SerializeToElement(schema);
    }

    private static JsonElement WithDefinitions()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse(Output.GetRawText())!;
        schema["$defs"] = System.Text.Json.Nodes.JsonNode.Parse(Input.GetProperty("$defs").GetRawText());
        return JsonSerializer.SerializeToElement(schema);
    }
}
