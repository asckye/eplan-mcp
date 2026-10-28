using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>Staged full-profile contract; registration waits for T4b/T4c execution verification.</summary>
internal static class PlacementCreateContract
{
    internal static readonly JsonElement Input = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["project","page","items"],"properties":{
          "project":{"type":"string","minLength":1},"page":{"type":"string","minLength":1},
          "items":{"type":"array","minItems":1,"maxItems":50,"items":{"$ref":"#/$defs/item"}},
          "snap_to_grid":{"type":"boolean","default":true},"dry_run":{"type":"boolean","default":false},
          "response_format":{"type":"string","enum":["concise","detailed"],"default":"concise"}},"$defs":{
          "point":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number","minimum":0,"maximum":10000}},
          "pair":{"type":"array","minItems":2,"maxItems":2,"items":{"$ref":"#/$defs/point"}},
          "item":{"oneOf":[
            {"type":"object","additionalProperties":false,"required":["kind","position","contents","height_mm"],"properties":{
              "kind":{"const":"text"},"position":{"$ref":"#/$defs/point"},"height_mm":{"type":"number","minimum":0.5,"maximum":100},
              "contents":{"type":"object","minProperties":1,"propertyNames":{"minLength":5,"maxLength":5,"pattern":"^([a-z]{2}_[A-Z]{2}|\\?\\?_\\?\\?)$"},"additionalProperties":{"type":"string","minLength":1,"maxLength":500}}}},
            {"type":"object","additionalProperties":false,"required":["kind","points"],"properties":{"kind":{"const":"line"},"points":{"$ref":"#/$defs/pair"}}},
            {"type":"object","additionalProperties":false,"required":["kind","corners"],"properties":{"kind":{"const":"rectangle"},"corners":{"$ref":"#/$defs/pair"}}}
          ]}}}
        """).RootElement.Clone();

    internal static readonly JsonElement Output = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["project","page","execution","dryRun","gridSize","boundarySource","items","changes","connectionChanges"],"properties":{
          "project":{"type":"string"},"page":{"type":"string"},"execution":{"const":"not_started"},"dryRun":{"const":true},
          "gridSize":{"type":"number","exclusiveMinimum":0},"boundarySource":{"const":"Page.Size"},
          "items":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"object","additionalProperties":false,"required":["requested","actual","bounds","snapped"],"properties":{
            "requested":{"$ref":"#/$defs/item"},"actual":{"$ref":"#/$defs/item"},"bounds":{"type":"array","minItems":4,"maxItems":4,"items":{"type":"number"}},"snapped":{"type":"boolean"}}}},
          "changes":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"string"}},"connectionChanges":{"type":"array","maxItems":0}}}
        """).RootElement.Clone();

    internal static Tool Definition => new()
    {
        Name = "eplan_placement_create",
        Title = "Create text and graphics on an EPLAN page",
        Description = "Plans 1-50 text, line or rectangle items on one Circuit page (dry_run only). Lines are graphics. " +
            "Coordinates: mm, bottom left, y up; snaps to grid and checks full outlines. Text needs ??_?? or the project display language; otherwise EPLAN does not show it (A54). " +
            "Execution returns CAPABILITY_MISSING until available; outside scratch will need approval. " +
            "Example: {\"project\":\"Line1\",\"page\":\"P\",\"items\":[{\"kind\":\"line\",\"points\":[[10,10],[20,20]]}],\"dry_run\":true}.",
        InputSchema = Input,
        OutputSchema = WithDefinitions(),
        Annotations = new() { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false },
    };

    private static JsonElement WithDefinitions()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse(Output.GetRawText())!;
        schema["$defs"] = System.Text.Json.Nodes.JsonNode.Parse(Input.GetProperty("$defs").GetRawText());
        return JsonSerializer.SerializeToElement(schema);
    }
}
