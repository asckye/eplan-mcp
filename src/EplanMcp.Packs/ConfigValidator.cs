using System.Collections.Frozen;
using System.Text.Json;
using static EplanMcp.Packs.BindingReadModel;

namespace EplanMcp.Packs;

/// <summary>Pure configuration checks; merging, required values and constraint intersections belong to C2.</summary>
public static class ConfigValidator
{
    /// <summary>Registered units and their factors to the dimension's reference unit, for C2 comparisons.</summary>
    public static IReadOnlyDictionary<string, (string Dimension, decimal Factor)> Units { get; } =
        new Dictionary<string, (string, decimal)>(StringComparer.Ordinal)
        {
            ["W"] = ("power", 1m),
            ["kW"] = ("power", 1000m),
            ["V"] = ("voltage", 1m),
            ["kV"] = ("voltage", 1000m),
            ["A"] = ("current", 1m),
            ["mA"] = ("current", 0.001m),
            ["mm"] = ("length", 0.001m),
            ["m"] = ("length", 1m),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly string[] _layers = ["base", "company", "cabinet_type", "project", "cabinet"];

    public static void Schema(JsonElement spec)
    {
        Object(spec, "/spec", "items", "");
        FiniteNumbers(spec, "/spec");
        JsonElement items = spec.GetProperty("items");
        Require(items.ValueKind == JsonValueKind.Object, "/spec/items", "must be keyed by configuration name");
        foreach (JsonProperty entry in items.EnumerateObject())
        {
            string at = Child("/spec/items", entry.Name);
            Require(Name(entry.Name), at, "must be a configuration name");
            JsonElement item = entry.Value;
            Object(item, at, "type allowedLayers required", "unit enum min max default");
            string? type = item.GetProperty("type").ValueKind == JsonValueKind.String ? item.GetProperty("type").GetString() : null;
            Require(type is "text" or "number" or "boolean" or "choice", at + "/type", "must be text, number, boolean or choice");
            Require(item.GetProperty("required").ValueKind is JsonValueKind.True or JsonValueKind.False, at + "/required", "must be boolean");
            Require(!item.TryGetProperty("unit", out JsonElement unit) || type == "number" && unit.ValueKind == JsonValueKind.String
                && Units.ContainsKey(unit.GetString()!), at + "/unit", "must be a registered unit on a number");
            foreach (string bound in new[] { "min", "max" })
            {
                Require(!item.TryGetProperty(bound, out JsonElement limit) || type == "number" && limit.ValueKind == JsonValueKind.Number, at + "/" + bound, "must be a number bound on a number");
            }

            Require(!item.TryGetProperty("min", out JsonElement min) || !item.TryGetProperty("max", out JsonElement max)
                || min.GetDecimal() <= max.GetDecimal(), at + "/max", "must be at least min");
            JsonElement layers = item.GetProperty("allowedLayers");
            Require(layers.ValueKind == JsonValueKind.Array && layers.GetArrayLength() > 0, at + "/allowedLayers", "must be a nonempty layer list");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach ((JsonElement layer, int index) in layers.EnumerateArray().Select((value, index) => (value, index)))
            {
                Require(layer.ValueKind == JsonValueKind.String && _layers.Contains(layer.GetString(), StringComparer.Ordinal)
                    && seen.Add(layer.GetString()!), $"{at}/allowedLayers/{index}", "must be a unique registered layer");
            }

            bool hasEnum = item.TryGetProperty("enum", out JsonElement choices);
            Require(type != "choice" || hasEnum, at + "/enum", "is required for choice");
            if (hasEnum)
            {
                Require(choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0, at + "/enum", "must be a nonempty set");
                JsonElement[] values = [.. choices.EnumerateArray()];
                for (int i = 0; i < values.Length; i++)
                {
                    Value(item, values[i], $"{at}/enum/{i}", checkEnum: false);
                    Require(!values.Take(i).Any(v => JsonElement.DeepEquals(v, values[i])), $"{at}/enum/{i}", "must be unique");
                }
            }

            if (item.TryGetProperty("default", out JsonElement fallback))
            {
                Require(seen.Contains("base"), at + "/default", "requires base in allowedLayers");
                Value(item, fallback, at + "/default");
            }
        }
    }

    /// <summary>Resolves local schema ids to their specs; a missing source is never treated as an empty schema.</summary>
    public static void Layer(JsonElement spec, Func<string, JsonElement?> resolve)
    {
        Object(spec, "/spec", "layer schema settings", "");
        JsonElement source = spec.GetProperty("schema");
        string reference = source.ValueKind == JsonValueKind.String ? source.GetString()! : string.Empty;
        // Pack checking has no dependency resource resolver yet; cross-pack lock resolution belongs to C2.
        Require(reference.StartsWith("config_schema/", StringComparison.Ordinal) && PackManifestValidator.IsId(reference[14..]), "/spec/schema", "must name a local config_schema/name");
        JsonElement? schema = resolve(reference);
        Require(schema.HasValue, "/spec/schema", "must reference an existing config_schema resource");
        // Schema defaults are the base layer and binding values hold project and cabinet, so packs carry only these two.
        JsonElement layer = spec.GetProperty("layer");
        Require(layer.ValueKind == JsonValueKind.String && layer.GetString() is "company" or "cabinet_type", "/spec/layer", "must be company or cabinet_type in a pack");
        ValidateSettings(schema!.Value, spec.GetProperty("settings"), layer.GetString()!);
    }

    /// <summary>Also validates binding v2 projectValues and cabinet values, using their own JSON pointer.</summary>
    public static void ValidateSettings(JsonElement schema, JsonElement settings, string layer, string at = "/spec/settings")
    {
        Schema(schema);
        Require(_layers.Contains(layer, StringComparer.Ordinal), "/spec/layer", "must be base, company, cabinet_type, project or cabinet");
        FiniteNumbers(settings, at);
        Settings(settings, at);
        foreach (JsonProperty entry in settings.EnumerateObject())
        {
            string pointer = Child(at, entry.Name);
            Require(Name(entry.Name) && schema.GetProperty("items").TryGetProperty(entry.Name, out _), pointer, "must name a key in the schema");
            JsonElement item = schema.GetProperty("items").GetProperty(entry.Name);
            Require(item.GetProperty("allowedLayers").EnumerateArray().Any(v => v.GetString() == layer), pointer, "is not allowed at this layer");
            if (entry.Value.TryGetProperty("default", out JsonElement fallback))
            {
                Value(item, fallback, pointer + "/default");
            }

            if (entry.Value.TryGetProperty("constraint", out JsonElement constraint))
            {
                JsonProperty rule = constraint.EnumerateObject().Single();
                string path = pointer + "/constraint/" + rule.Name;
                if (rule.Name == "allowed" && rule.Value.ValueKind == JsonValueKind.Object)
                {
                    Require(item.GetProperty("type").GetString() == "number", path, "intervals require a number key");
                    Value(item, rule.Value.GetProperty("min"), path + "/min");
                    Value(item, rule.Value.GetProperty("max"), path + "/max");
                    Require(rule.Value.GetProperty("min").GetDecimal() <= rule.Value.GetProperty("max").GetDecimal(), path + "/max", "must be at least min");
                }
                else if (rule.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach ((JsonElement value, int index) in rule.Value.EnumerateArray().Select((value, index) => (value, index)))
                    {
                        Value(item, value, $"{path}/{index}");
                        Require(!rule.Value.EnumerateArray().Take(index).Any(v => JsonElement.DeepEquals(v, value)), $"{path}/{index}", "must be unique");
                    }
                }
                else
                {
                    Value(item, rule.Value, path);
                }
            }
        }
    }

    private static bool Name(string name) => PackManifestValidator.IsId(name.Replace('_', '-'));

    private static void Value(JsonElement item, JsonElement value, string at, bool checkEnum = true)
    {
        string type = item.GetProperty("type").GetString()!;
        Require(type switch
        {
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => value.ValueKind == JsonValueKind.String,
        }, at, "must match the schema type without coercion");
        Require(type != "number" || (!item.TryGetProperty("min", out JsonElement min) || value.GetDecimal() >= min.GetDecimal())
            && (!item.TryGetProperty("max", out JsonElement max) || value.GetDecimal() <= max.GetDecimal()), at, "must lie within the schema bounds");
        Require(!checkEnum || !item.TryGetProperty("enum", out JsonElement choices) || choices.EnumerateArray().Any(v => JsonElement.DeepEquals(v, value)), at, "must be in the schema enum");
    }
}
