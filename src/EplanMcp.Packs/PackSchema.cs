using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace EplanMcp.Packs;

/// <summary>
/// The JSON Schemas (draft 2020-12) of the pack files, generated from the types with <see cref="PackJson.Options"/>, so
/// they cannot drift from what EplanMcp reads. The committed copies in <c>schemas/</c> are checked against these by a
/// test; other programs validate with them.
/// </summary>
public static class PackSchema
{
    public const string ManifestId = "https://github.com/asckye/eplan-mcp/schemas/pack-manifest.schema.json";

    public const string LockId = "https://github.com/asckye/eplan-mcp/schemas/pack-lock.schema.json";

    public static JsonObject Manifest()
    {
        JsonObject schema = Generate(typeof(PackManifest), ManifestId, "EplanMcp pack manifest (pack.json), schemaVersion 1 or 2");
        schema["properties"]!["schemaVersion"]!.AsObject().Remove("const");
        schema["properties"]!["schemaVersion"]!["enum"] = new JsonArray(1, 2);
        return schema;
    }

    public static JsonObject Lock() => Generate(typeof(PackLock), LockId, "EplanMcp pack lock (pack.lock.json), lockVersion 1");

    public const string BindingId = "https://github.com/asckye/eplan-mcp/schemas/project-binding.schema.json";

    public static JsonObject Binding()
    {
        JsonObject schema = Generate(typeof(ProjectBinding), BindingId, "EplanMcp project binding (binding.json in a project workspace), bindingVersion 1");
        JsonObject properties = schema["properties"]!.AsObject();
        JsonObject cabinet = properties["cabinets"]!["items"]!["properties"]!.AsObject();
        foreach (string name in new[] { "projectValues", "packRoots", "layers", "values", "structure" })
        {
            (name is "projectValues" or "packRoots" ? properties : cabinet).Remove(name);
        }

        return schema;
    }

    /// <summary>A schema as the committed file holds it: the bytes <see cref="PackJson.Serialize"/> writes.</summary>
    public static string Text(JsonObject schema) => schema.ToJsonString(PackJson.Options) + "\n";

    internal static JsonObject Generate(Type type, string id, string title)
    {
        var exporter = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true, TransformSchemaNode = Transform };
        JsonObject generated = PackJson.Options.GetJsonSchemaAsNode(type, exporter).AsObject();
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = id,
            ["title"] = title,
        };
        foreach (string key in generated.Select(p => p.Key).ToList())
        {
            JsonNode? value = generated[key];
            generated.Remove(key);
            schema[key] = value;
        }

        return schema;
    }

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode node)
    {
        if (node is not JsonObject schema)
        {
            return node;
        }

        ICustomAttributeProvider? property = context.PropertyInfo?.AttributeProvider;
        string? description = Attribute<DescriptionAttribute>(property)?.Description
            ?? context.TypeInfo.Type.GetCustomAttribute<DescriptionAttribute>()?.Description;
        if (description is not null && !schema.ContainsKey("$ref"))
        {
            schema["description"] = description;
        }

        if (Attribute<SchemaAttribute>(property) is { } rules)
        {
            if (rules.Pattern is not null)
            {
                (schema["items"] as JsonObject ?? schema)["pattern"] = rules.Pattern;
            }

            if (rules.Minimum != long.MinValue)
            {
                schema["minimum"] = rules.Minimum;
            }

            if (rules.Const != int.MinValue)
            {
                schema["const"] = rules.Const;
            }
        }

        return schema;
    }

    private static T? Attribute<T>(ICustomAttributeProvider? provider)
        where T : Attribute =>
        provider?.GetCustomAttributes(typeof(T), inherit: false).OfType<T>().FirstOrDefault();
}
