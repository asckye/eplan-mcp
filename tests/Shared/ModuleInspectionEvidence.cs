using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Packs;

namespace EplanMcp.Tests.Shared;

internal static class ModuleInspectionEvidence
{
    public static string Hash => new('a', 64);
    public static JsonObject Create(string? hash = null)
    {
        var root = JsonNode.Parse("""
            {
              "macroHash": "", "apiExtensionLicence": true, "complete": true,
              "coverage": { "functions": true, "pages": true, "connections": true, "crossPageReferences": true, "placeholderBindings": true, "properties": true },
              "objects": [
                { "id": "page/1", "kind": "page", "name": "=AP+A1/1", "page": null, "properties": [] },
                { "id": "page/2", "kind": "page", "name": "=AP+A1/2", "page": null, "properties": [] },
                { "id": "function/1", "kind": "function", "name": "=AP+A1-K1", "page": "=AP+A1/1", "properties": [
                  { "propertyId": 20011, "index": 0, "language": "en_US", "state": "value", "value": "Motor" },
                  { "propertyId": 20011, "index": 0, "language": "de_DE", "state": "value", "value": "Motor DE" }
                ] },
                { "id": "function/2", "kind": "function", "name": "=AP+A1-K1", "page": "=AP+A1/2", "properties": [] },
                { "id": "connection/1", "kind": "connection", "name": "1>2", "page": "=AP+A1/1", "properties": [] },
                { "id": "placeholder/1", "kind": "placeholder", "name": "starter", "page": "=AP+A1/1", "properties": [] }
              ],
              "references": [
                { "from": "function/1", "to": "function/2", "kind": "cross_reference" },
                { "from": "connection/1", "to": "function/1", "kind": "connection" },
                { "from": "connection/1", "to": "function/2", "kind": "connection" }
              ],
              "placeholders": [
                { "object": "placeholder/1", "name": "starter", "variables": ["text"],
                  "valueSets": [ { "name": "motor", "values": { "text": [
                    { "language": "en_US", "value": "Motor" }, { "language": "de_DE", "value": "Motor DE" }
                  ] } } ],
                  "bindings": [ { "variable": "text", "target": "function/1", "propertyId": 20011, "index": 0, "language": "en_US" } ]
                }
              ],
              "gaps": []
            }
            """)!.AsObject();
        root["macroHash"] = hash ?? Hash;
        return root;
    }

    public static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node, PackJson.Options);
}
