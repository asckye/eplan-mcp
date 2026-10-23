using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>The discovery contract for the workflow context tool.</summary>
internal static class ContextSchema
{
    private static readonly JsonElement _scopeSchema = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"properties":{
          "pages":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","minLength":1}},
          "structures":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","pattern":"^(?:[=+&#][^=+&#/*?\\s]+)+$"}},
          "kinds":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","enum":["page","function","connection"]}},
          "locators":{"type":"array","minItems":1,"maxItems":200,"uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["project","kind","type","oid"],"properties":{
            "project":{"type":"string","minLength":1},"kind":{"type":"string","enum":["page","function","connection"]},"type":{"type":"integer","minimum":1},"oid":{"type":"integer","minimum":1},
            "name":{"type":["string","null"]},"page":{"type":["string","null"]},"category":{"type":["string","null"]},"pos":{"type":["string","null"],"pattern":"^-?[0-9]+(?:\\.[0-9]+)?,-?[0-9]+(?:\\.[0-9]+)?$"}}}}}}
        """).RootElement.Clone();

    private static readonly JsonElement _includeSchema = JsonDocument.Parse("""
        {"type":"array","minItems":1,"uniqueItems":true,"default":["objects","facts","modules","config","capabilities"],"items":{"type":"string","enum":["objects","facts","modules","rules","config","capabilities"]}}
        """).RootElement.Clone();

    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        McpServerTool tool = tools.Single(t => t.ProtocolTool.Name == ContextTools.GetTool);
        var input = JsonNode.Parse("""
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["scope"],"properties":{
              "project":{"type":"string"},"cabinet":{"type":"string","enum":["default"],"default":"default"},"scope":{},"include":{},"max_tokens":{"type":"integer","minimum":1,"maximum":20000,"default":5000}}}
            """)!;
        input["properties"]!["scope"] = JsonNode.Parse(_scopeSchema.GetRawText());
        input["properties"]!["include"] = JsonNode.Parse(_includeSchema.GetRawText());
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(input);
        tool.ProtocolTool.OutputSchema = JsonDocument.Parse("""
            {"type":"object","additionalProperties":false,"required":["project","cabinet","scope","contextRef","sources","coverage","budget","truncated","hint"],"properties":{
              "project":{"type":"object","required":["name","path"],"properties":{"name":{"type":"string"},"path":{"type":"string"}}},"cabinet":{"type":"string","const":"default"},"scope":{"type":"object"},
              "contextRef":{"type":"object","required":["snapshotHash","generation","bindingRevision","lockDigest"],"properties":{"snapshotHash":{"type":["string","null"]},"generation":{"type":"null"},"bindingRevision":{"type":["integer","null"],"minimum":0},"lockDigest":{"type":["string","null"]}}},
              "objects":{"type":"array","items":{"type":"object"}},"facts":{"type":"array","items":{"type":"object"}},"modules":{"type":"array","items":{"type":"object"}},"rules":{"type":"array","items":{"type":"object"}},"config":{"type":"array","items":{"type":"object"}},"capabilities":{"type":"array","items":{"type":"object"}},"sources":{"type":"array","items":{"type":"object"}},
              "coverage":{"type":"object","required":["profile","format","pages","missing"],"properties":{"profile":{"type":"string"},"format":{"type":"string"},"pages":{"type":"array","items":{"type":"object","required":["name","type","oid","hash"],"properties":{"name":{"type":"string"},"type":{"type":"integer"},"oid":{"type":"integer"},"hash":{"type":"string"}}}},"missing":{"type":"array","items":{"type":"string"}}}},
              "budget":{"type":"object","required":["requestedTokens","estimatedTokens","omittedItems"],"properties":{"requestedTokens":{"type":"integer","minimum":0},"estimatedTokens":{"type":"integer","minimum":0},"omittedItems":{"type":"integer","minimum":0}}},"truncated":{"type":"boolean"},"hint":{"type":["string","null"]}}}
            """).RootElement.Clone();
    }
}
