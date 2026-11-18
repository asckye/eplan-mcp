using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>The discovery contract for the workflow context tool.</summary>
internal static class ContextSchema
{
    private static readonly JsonElement _scopeSchema = JsonDocument.Parse("""
        {"description":"Intersect the supplied object filters.","type":"object","additionalProperties":false,"properties":{
          "pages":{"description":"Exact full page names.","type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","minLength":1}},
          "structures":{"description":"Match complete structure segments.","type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","pattern":"^(?:[=+&#][^=+&#/*?\\s]+)+$"}},
          "kinds":{"description":"Restrict snapshot objects by kind.","type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","enum":["page","function","connection"]}},
          "locators":{"description":"Exact object locators from the snapshot.","type":"array","minItems":1,"maxItems":200,"uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["project","kind","type","oid"],"properties":{
            "project":{"description":"Project path from the snapshot locator.","type":"string","minLength":1},"kind":{"description":"Object kind from the snapshot.","type":"string","enum":["page","function","connection"]},"type":{"description":"EPLAN type number from the snapshot.","type":"integer","minimum":1},"oid":{"description":"Object identifier from the snapshot.","type":"integer","minimum":1},
            "name":{"description":"Expected object name; omit to skip this check.","type":["string","null"]},"page":{"description":"Full page name","type":["string","null"]},"category":{"description":"Expected function category.","type":["string","null"]},"pos":{"description":"Expected function position as x,y in mm.","type":["string","null"],"pattern":"^-?[0-9]+(?:\\.[0-9]+)?,-?[0-9]+(?:\\.[0-9]+)?$"}}}}}}
        """).RootElement.Clone();

    private static readonly JsonElement _includeSchema = JsonDocument.Parse("""
        {"description":"Context sections to return; request plugins explicitly.","type":"array","minItems":1,"uniqueItems":true,"default":["objects","facts","modules","config","capabilities"],"items":{"type":"string","enum":["plugins","objects","facts","modules","rules","config","capabilities"]}}
        """).RootElement.Clone();

    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        McpServerTool tool = tools.Single(t => t.ProtocolTool.Name == ContextTools.GetTool);
        var input = JsonNode.Parse("""
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{
              "project":{"description":"Open project; omit for offline global plugins.","type":["string","null"]},"cabinet":{"description":"Cabinet ID; required with multiple cabinets.","type":"string","minLength":1},"scope":{},"include":{},"max_tokens":{"description":"Response budget: 1-20000 tokens, default 5000.","type":"integer","minimum":1,"maximum":20000,"default":5000}}}
            """)!;
        input["properties"]!["scope"] = JsonNode.Parse(_scopeSchema.GetRawText());
        input["properties"]!["include"] = JsonNode.Parse(_includeSchema.GetRawText());
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(input);
        tool.ProtocolTool.OutputSchema = JsonDocument.Parse("""
            {"type":"object","additionalProperties":false,"required":["project","cabinet","scope","contextRef","sources","coverage","budget","truncated","hint"],"properties":{
              "project":{"type":["object","null"],"required":["name","path"],"properties":{"name":{"type":"string"},"path":{"type":"string"}}},"cabinet":{"type":["string","null"]},"scope":{"type":"object"},
              "contextRef":{"type":"object","required":["snapshotHash","generation","bindingRevision","lockDigest"],"properties":{"snapshotHash":{"type":["string","null"]},"generation":{"type":"null"},"bindingRevision":{"type":["integer","null"],"minimum":0},"lockDigest":{"type":["string","null"]}}},
              "plugins":{"type":"array","items":{"type":"object"}},"objects":{"type":"array","items":{"type":"object"}},"facts":{"type":"array","items":{"type":"object"}},"modules":{"type":"array","items":{"type":"object"}},"rules":{"type":"array","items":{"type":"object"}},"config":{"type":"array","items":{"type":"object"}},"capabilities":{"type":"array","items":{"type":"object"}},"sources":{"type":"array","items":{"type":"object"}},
              "coverage":{"type":"object","required":["profile","format","pages","missing"],"properties":{"profile":{"type":"string"},"format":{"type":"string"},"pages":{"type":"array","items":{"type":"object","required":["name","type","oid","hash"],"properties":{"name":{"type":"string"},"type":{"type":"integer"},"oid":{"type":"integer"},"hash":{"type":"string"}}}},"missing":{"type":"array","items":{"type":"string"}}}},
              "budget":{"type":"object","required":["requestedTokens","estimatedTokens","omittedItems"],"properties":{"requestedTokens":{"type":"integer","minimum":0},"estimatedTokens":{"type":"integer","minimum":0},"omittedItems":{"type":"integer","minimum":0}}},"truncated":{"type":"boolean"},"hint":{"type":["string","null"]}}}
            """).RootElement.Clone();
    }
}
