using System.Text.Json;
using System.Text.Json.Nodes;
using static EplanMcp.Packs.BindingReadModel;

namespace EplanMcp.Packs;

/// <summary>Pure five-layer merge of resolved specs; resource and lock resolution remain the caller's responsibility.</summary>
public static class ConfigMerger
{
    // Same values as EplanMcp.Contracts.ErrorReason; Packs stays free of the Contracts reference.
    public const string PackInvalid = "PACK_INVALID";
    public const string ConfigConflict = "CONFIG_CONFLICT";
    public const string InvalidArgument = "INVALID_ARGUMENT";

    public static ConfigMergeResult Merge(IReadOnlyList<ConfigDocument> schemas, IReadOnlyList<ConfigDocument> layers, ProjectBinding binding, int cabinetIndex)
    {
        var items = new JsonObject();
        var values = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var trace = new JsonArray();
        string reason = PackInvalid;
        try
        {
            foreach (ConfigDocument schema in schemas)
            {
                trace = [];
                JsonObject source = Source(schema, "config_schema/");
                AddTrace(trace, "base", source, "/spec", "schema", JsonNode.Parse(schema.Spec.GetRawText())!);
                ConfigValidator.Schema(schema.Spec);
                foreach (JsonProperty entry in schema.Spec.GetProperty("items").EnumerateObject())
                {
                    string at = Child("/spec/items", entry.Name);
                    JsonObject item = JsonNode.Parse(entry.Value.GetRawText())!.AsObject();
                    trace = values.TryGetValue(entry.Name, out JsonObject? previous) ? previous["trace"]!.DeepClone().AsArray() : [];
                    AddTrace(trace, "base", source, at, "schema", item);
                    reason = ConfigConflict;
                    Require(!items.TryGetPropertyValue(entry.Name, out JsonNode? existing) || JsonNode.DeepEquals(existing, ConvertItem(item, existing!["unit"], at)), at, "schemas define this key differently; align their definitions");
                    reason = PackInvalid;
                    if (previous is null)
                    {
                        items[entry.Name] = item.DeepClone();
                        var constraints = new JsonObject();
                        if (item["enum"] is { } choices)
                        {
                            constraints["allowed"] = choices.DeepClone();
                        }
                        else if (item.ContainsKey("min") || item.ContainsKey("max"))
                        {
                            constraints["allowed"] = new JsonObject(item.Where(p => p.Key is "min" or "max").Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
                        }

                        values[entry.Name] = previous = new JsonObject { ["key"] = entry.Name, ["value"] = item["default"]?.DeepClone(), ["unit"] = item["unit"]?.DeepClone(), ["constraints"] = constraints, ["trace"] = new JsonArray() };
                    }

                    if (item["default"] is { } fallback)
                    {
                        AddTrace(previous["trace"]!.AsArray(), "base", source, at + "/default", "default", fallback);
                    }

                    if (item["default"] is null || previous["constraints"]!.AsObject().Count > 0)
                    {
                        AddTrace(previous["trace"]!.AsArray(), "base", source, at, "schema", item);
                    }
                }
            }

            int rank = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigDocument document in layers)
            {
                reason = PackInvalid;
                JsonObject source = Source(document, "config_layer/");
                trace = [];
                AddTrace(trace, "pack", source, "/spec", "layer", JsonNode.Parse(document.Spec.GetRawText())!);
                Object(document.Spec, "/spec", "layer schema settings", "");
                string? layer = document.Spec.GetProperty("layer").ValueKind == JsonValueKind.String ? document.Spec.GetProperty("layer").GetString() : null;
                int next = layer switch { "company" => 1, "cabinet_type" => 2, _ => 0 };
                Require(next > 0 && next >= rank && seen.Add(document.Resource.Split(':')[0] + ":" + layer), "/spec/layer", "must follow company then cabinet_type, once per pack and layer");
                rank = next;
                string? reference = document.Spec.GetProperty("schema").ValueKind == JsonValueKind.String ? document.Spec.GetProperty("schema").GetString() : null;
                ConfigDocument? schema = schemas.FirstOrDefault(s => s.Resource == reference || s.Resource == document.Resource.Split(':')[0] + ":" + reference);
                Require(schema is not null, "/spec/schema", "must name a supplied resolved schema");
                Apply(schema!.Spec, document.Spec.GetProperty("settings"), layer!, source, "/spec/settings");
            }

            reason = InvalidArgument;
            trace = [];
            var bindingSource = new JsonObject { ["project"] = binding.Project, ["bindingRevision"] = binding.Revision };
            AddTrace(trace, "project", bindingSource, "", "binding", JsonSerializer.SerializeToNode(binding, PackJson.Options)!);
            Require(binding.BindingVersion == 2, "/bindingVersion", "configuration merging requires binding v2");
            BindingReadModel.Validate(JsonSerializer.SerializeToElement(binding, PackJson.Options));
            Require(cabinetIndex >= 0 && cabinetIndex < binding.Cabinets.Count, "/cabinets", "select an existing cabinet");
            var mergedSchema = new JsonObject { ["items"] = items.DeepClone() };
            Apply(Element(mergedSchema), Element(binding.ProjectValues!), "project", bindingSource, "/projectValues");
            reason = InvalidArgument;
            Apply(Element(mergedSchema), Element(binding.Cabinets[cabinetIndex].Values!), "cabinet", bindingSource, $"/cabinets/{cabinetIndex}/values");
            foreach ((string key, JsonObject value) in values)
            {
                reason = ConfigConflict;
                trace = value["trace"]!.AsArray();
                Require(value["value"] is not null || !items[key]!["required"]!.GetValue<bool>(), Child("/spec/items", key) + "/required", "requires an explicit default value");
                Require(value["value"] is null || Contains(value["constraints"]!.AsObject(), value["value"]!), trace.LastOrDefault(t => t!["kind"]!.GetValue<string>() == "default")?["pointer"]?.GetValue<string>() ?? Child("/spec/items", key), "final default is outside the merged constraint; change the default or constraints");
            }

            return new(Element(new JsonArray(values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (JsonNode)p.Value).ToArray())), null, null, null, Element(new JsonArray()));
        }
        catch (PackFormatException ex)
        {
            int separator = ex.Message.IndexOf(':', StringComparison.Ordinal);
            return new(Element(new JsonArray()), reason, ex.Message[..separator], ex.Message[(separator + 2)..], Element(trace));
        }

        void Apply(JsonElement schema, JsonElement settings, string layer, JsonObject source, string at)
        {
            trace = [];
            AddTrace(trace, layer, source, at, "settings", JsonNode.Parse(settings.GetRawText())!);
            ConfigValidator.ValidateSettings(schema, settings, layer, at);
            foreach (JsonProperty entry in settings.EnumerateObject())
            {
                JsonObject value = values[entry.Name];
                trace = value["trace"]!.AsArray();
                foreach (JsonProperty setting in entry.Value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    string pointer = Child(Child(at, entry.Name), setting.Name);
                    JsonNode content = JsonNode.Parse(setting.Value.GetRawText())!;
                    AddTrace(trace, layer, source, pointer, setting.Name, content);
                    reason = ConfigConflict;
                    content = ConvertNumbers(content, schema.GetProperty("items").GetProperty(entry.Name).TryGetProperty("unit", out JsonElement unit) ? unit.GetString() : null, value["unit"]?.GetValue<string>(), pointer);
                    if (setting.Name == "default")
                    {
                        value["value"] = content;
                    }
                    else
                    {
                        Require(Subset(content.AsObject(), value["constraints"]!.AsObject()), pointer, "constraint widens an earlier constraint or has an empty intersection; narrow it");
                        value["constraints"] = content;
                    }
                }
            }
        }
    }

    private static bool Contains(JsonObject constraint, JsonNode value) => constraint["fixed"] is { } fixedValue ? JsonNode.DeepEquals(fixedValue, value)
        : constraint["allowed"] is JsonArray set ? set.Any(v => JsonNode.DeepEquals(v, value))
        : constraint["allowed"] is not JsonObject interval || (interval["min"] is null || value.GetValue<decimal>() >= interval["min"]!.GetValue<decimal>())
            && (interval["max"] is null || value.GetValue<decimal>() <= interval["max"]!.GetValue<decimal>());

    private static bool Subset(JsonObject lower, JsonObject upper)
    {
        if (lower["fixed"] is { } fixedValue)
        {
            return Contains(upper, fixedValue);
        }

        if (lower["allowed"] is JsonArray set)
        {
            return set.All(v => Contains(upper, v!));
        }

        JsonObject interval = lower["allowed"]!.AsObject();
        decimal min = interval["min"]!.GetValue<decimal>(), max = interval["max"]!.GetValue<decimal>();
        return upper["allowed"] is JsonArray choices ? min == max && choices.Any(v => v!.GetValue<decimal>() == min)
            : Contains(upper, interval["min"]!) && Contains(upper, interval["max"]!);
    }

    private static JsonObject ConvertItem(JsonObject item, JsonNode? targetUnit, string at)
    {
        JsonObject converted = ConvertNumbers(item, item["unit"]?.GetValue<string>(), targetUnit?.GetValue<string>(), at).AsObject();
        if (targetUnit is not null)
        {
            converted["unit"] = targetUnit.DeepClone();
        }

        return converted;
    }

    private static JsonNode ConvertNumbers(JsonNode node, string? from, string? to, string at)
    {
        Require(from == to || from is not null && to is not null && ConfigValidator.Units[from].Dimension == ConfigValidator.Units[to].Dimension, at + "/unit", "units must have the same dimension");
        decimal factor = from == to ? 1m : ConfigValidator.Units[from!].Factor / ConfigValidator.Units[to!].Factor;
        if (node is JsonValue scalar)
        {
            if (scalar.GetValueKind() != JsonValueKind.Number)
            {
                return node.DeepClone();
            }

            decimal number = scalar.GetValue<decimal>();
            Require(factor <= 1 || Math.Abs(number) <= decimal.MaxValue / factor, at, "converted number exceeds the finite decimal range");
            decimal converted = number * factor;
            Require(converted / factor == number, at, "unit conversion must preserve decimal precision");
            return JsonValue.Create(converted)!;
        }

        return node is JsonArray array ? new JsonArray(array.Select((v, i) => ConvertNumbers(v!, from, to, $"{at}/{i}")).ToArray())
            : new JsonObject(node.AsObject().Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, ConvertNumbers(p.Value!, from, to, Child(at, p.Key)))));
    }

    private static JsonObject Source(ConfigDocument document, string kind)
    {
        Require(AssetNames.TryParse(document.Resource, out _, out string? version, out string? local) && SemanticVersion.TryParse(version, out _) && local.StartsWith(kind, StringComparison.Ordinal) && PackManifestValidator.IsId(local[kind.Length..])
            && PackManifestValidator.IsSha256(document.Sha256), "/source", "requires an exact configuration resource id and SHA-256");
        return new JsonObject { ["resource"] = document.Resource, ["sha256"] = document.Sha256 };
    }

    private static void AddTrace(JsonArray trace, string layer, JsonObject source, string pointer, string kind, JsonNode value) =>
        trace.Add(new JsonObject { ["layer"] = layer, ["source"] = source.DeepClone(), ["pointer"] = pointer, ["kind"] = kind, ["value"] = value.DeepClone() });

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node, PackJson.Options);
}
