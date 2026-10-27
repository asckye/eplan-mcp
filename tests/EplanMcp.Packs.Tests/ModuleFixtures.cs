using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

internal static class ModuleFixtures
{
    public static readonly PackResource[] Resources =
    [
        Manifests.File("resources/macros/Motor.ema", ResourceKind.Macro),
        Manifests.File("resources/docs/guide.md", ResourceKind.Document, 'b'),
        Manifests.File("knowledge/material_mapping/company-a.json", ResourceKind.MaterialMapping),
        Manifests.File("knowledge/material_mapping/company-b.json", ResourceKind.MaterialMapping),
        Manifests.File("knowledge/sample/motor-acceptance.json", ResourceKind.Sample),
        Manifests.File("knowledge/workflow/motor.json", ResourceKind.Workflow),
        Manifests.File("knowledge/rule/starter-allowed.json", ResourceKind.Rule),
        Manifests.File(ConfigFixtures.CompanyPath, ResourceKind.ConfigLayer),
    ];

    public static PackResource? Resolve(string id) => Resources.FirstOrDefault(r => AssetNames.Local(r) == id);

    public static RuleValueType? Config(string name) => name switch
    {
        "motor.starter" or "motor.mapping" => new("text"),
        "motor.power" => new("number", "kW"),
        _ => null,
    };

    public static KnowledgeDocument Document(string name) => KnowledgeDocument.Read(ConfigFixtures.Read("Modules/" + name + ".json"));

    public static JsonObject Body(string name) => (JsonObject)Document(name).Spec.DeepClone();

    public static JsonElement Spec(string name) => Element(Body(name));

    public static JsonElement Element(JsonNode body) => JsonSerializer.SerializeToElement(body, PackJson.Options);

    public static void Set(JsonNode body, string path, string json)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        JsonNode parent = body;
        foreach (string part in parts.SkipLast(1))
        {
            parent = parent is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)]! : parent[part]!;
        }

        if (parent is JsonArray items)
        {
            items[int.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture)] = JsonNode.Parse(json);
        }
        else
        {
            parent[parts[^1]] = JsonNode.Parse(json);
        }
    }

    public static ModuleDefinition Module(JsonNode body) => ModuleValidator.Module(Element(body), Resolve, Config);
}
