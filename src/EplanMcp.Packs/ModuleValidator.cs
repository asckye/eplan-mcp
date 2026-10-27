using System.Text.Json;
using System.Text.Json.Nodes;
using static EplanMcp.Packs.BindingReadModel;

namespace EplanMcp.Packs;

/// <summary>V2 metadata contracts only; native macro inspection and engineering confirmation remain separate gates.</summary>
public static class ModuleValidator
{
    public static ModuleDefinition Module(JsonElement spec, Func<string, PackResource?> resolve, Func<string, RuleValueType?>? config = null)
    {
        Object(spec, "/spec", "macro parameters valueSets interface partMapping applicability acceptance", "description extraction");
        FiniteNumbers(spec, "/spec");
        JsonElement macro = spec.GetProperty("macro");
        Object(macro, "/spec/macro", "asset hash representation variant numbering", "");
        PackResource resource = Reference(macro.GetProperty("asset"), "macro", "/spec/macro/asset", resolve);
        Require(resource.Path.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) && macro.GetProperty("hash").ValueKind == JsonValueKind.String && macro.GetProperty("hash").GetString() == resource.Sha256, "/spec/macro/hash", "requires a window macro with its exact hash");
        Require(Text(macro.GetProperty("representation")) == "multi_line" && Integer(macro.GetProperty("variant"), 0) && macro.GetProperty("variant").GetInt32() == 0 && Text(macro.GetProperty("numbering")) == "keep", "/spec/macro", "only multi_line, variant 0, numbering keep is supported");
        JsonElement schema = Parameters(spec.GetProperty("parameters"), bindings: true);
        var names = new HashSet<string>(StringComparer.Ordinal);
        Each(spec.GetProperty("valueSets"), "/spec/valueSets", 1, 50, (set, at) =>
        {
            Object(set, at, "name values", "");
            Require(Text(set.GetProperty("name")) is { } name && names.Add(name), at + "/name", "must be a unique value-set name");
            Values(schema, set.GetProperty("values"), at + "/values");
        });
        Object(spec.GetProperty("interface"), "/spec/interface", "ports", "");
        var ports = new HashSet<string>(StringComparer.Ordinal);
        Each(spec.GetProperty("interface").GetProperty("ports"), "/spec/interface/ports", 0, 50, (port, at) =>
        {
            Object(port, at, "name kind object direction connectionPoint", "");
            Require(Id(port.GetProperty("name")) && ports.Add(port.GetProperty("name").GetString()!), at + "/name", "must be a unique port identifier");
            Require(Text(port.GetProperty("kind")) is "terminal" or "potential" or "cable_end", at + "/kind", "must be terminal, potential or cable_end");
            Require(Id(port.GetProperty("object")), at + "/object", "must be a macro-relative object identifier");
            Require(Text(port.GetProperty("direction")) is "in" or "out" or "inout", at + "/direction", "must be in, out or inout");
            Require(Text(port.GetProperty("connectionPoint")) is not null, at + "/connectionPoint", "must name a connection point");
        });
        JsonElement mapping = spec.GetProperty("partMapping");
        Object(mapping, "/spec/partMapping", "", "asset configKey");
        Require(mapping.EnumerateObject().Count() == 1, "/spec/partMapping", "must contain exactly one asset or configKey");
        if (mapping.TryGetProperty("asset", out JsonElement asset))
        {
            Reference(asset, "material_mapping", "/spec/partMapping/asset", resolve);
        }
        else
        {
            Require(Text(mapping.GetProperty("configKey")) is { } key && config?.Invoke(key) == new RuleValueType("text"), "/spec/partMapping/configKey", "must name a declared text configuration key");
        }
        RuleNode applicability = Predicate(spec.GetProperty("applicability"), "/spec/applicability", schema, config);
        bool InputsOnly(RuleNode node) => node.Operator is not ("property" or "some" or "every" or "count") && node.Operands.All(InputsOnly);
        Require(InputsOnly(applicability), "/spec/applicability", "applicability requires configuration or parameter inputs with declared coverage");
        JsonElement acceptance = spec.GetProperty("acceptance");
        Object(acceptance, "/spec/acceptance", "checks samples", "");
        Each(acceptance.GetProperty("checks"), "/spec/acceptance/checks", 1, 50, (check, at) =>
        {
            Require(check.ValueKind == JsonValueKind.Object && check.TryGetProperty("kind", out _), at + "/kind", "is required");
            string? kind = Text(check.GetProperty("kind"));
            Require(kind is "object" or "property" or "port" or "unchanged_outside", at + "/kind", "must name a supported acceptance check");
            Object(check, at, "kind source coverage " + (kind switch { "object" => "object", "property" => "object propertyId expected", "port" => "port", _ => "" }), kind == "property" ? "language" : "");
            foreach (JsonProperty member in check.EnumerateObject().Where(p => p.Name is "object" or "propertyId" or "language" or "expected" or "port"))
            {
                JsonElement value = member.Value;
                Require(member.Name switch { "propertyId" => Integer(value, 1), "expected" => Scalar(value), "port" => Text(value) is { } p && ports.Contains(p), "object" => Id(value), _ => Text(value) is not null }, at + "/" + member.Name, "has an invalid acceptance target or value");
            }
            Source(check.GetProperty("source"), at + "/source", resolve);
            Each(check.GetProperty("coverage"), at + "/coverage", 1, 20, (v, p) => Require(Text(v) is not null, p, "must name required coverage"));
        });
        Each(acceptance.GetProperty("samples"), "/spec/acceptance/samples", 1, 20, (v, p) => Reference(v, "sample", p, resolve));
        Require(!spec.TryGetProperty("description", out JsonElement description) || Text(description) is not null, "/spec/description", "must be nonempty text");
        if (spec.TryGetProperty("extraction", out JsonElement extraction))
        {
            Object(extraction, "/spec/extraction", "source sourceVersion snapshotHash extractorVersion pages locators objects observations", "");
            Source(extraction.GetProperty("source"), "/spec/extraction/source", resolve);
            Require(Text(extraction.GetProperty("sourceVersion")) is not null && Text(extraction.GetProperty("extractorVersion")) is not null && Text(extraction.GetProperty("snapshotHash")) is { } hash && PackManifestValidator.IsSha256(hash), "/spec/extraction", "requires versions and snapshot SHA-256");
            foreach (string list in new[] { "pages", "locators" })
            {
                Each(extraction.GetProperty(list), "/spec/extraction/" + list, 1, 200, (v, p) => Require(Locator(v), p, "must be a stable source locator without a machine path"));
            }
            Require(extraction.GetProperty("objects").ValueKind == JsonValueKind.Object && extraction.GetProperty("objects").EnumerateObject().All(entry => PackManifestValidator.IsId(entry.Name) && Locator(entry.Value)), "/spec/extraction/objects", "must map relative identifiers to stable source locators");
            Each(extraction.GetProperty("observations"), "/spec/extraction/observations", 1, 50, (v, p) => Values(schema, v, p));
        }
        return new(spec.Clone(), schema, applicability);
    }

    public static MaterialMappingDefinition Mapping(JsonElement spec, Func<string, PackResource?> resolve)
    {
        Object(spec, "/spec", "parameters entries", "");
        FiniteNumbers(spec, "/spec");
        JsonElement schema = Parameters(spec.GetProperty("parameters"), bindings: false);
        var previous = new List<JsonElement>();
        Each(spec.GetProperty("entries"), "/spec/entries", 1, 500, (entry, at) =>
        {
            Object(entry, at, "when part variant quantity source", "");
            JsonElement when = entry.GetProperty("when");
            Values(schema, when, at + "/when", partial: true);
            Require(when.EnumerateObject().Any(), at + "/when", "must contain typed parameter values");
            Require(!previous.Any(prior => !when.EnumerateObject().Any(p => prior.TryGetProperty(p.Name, out JsonElement other) && !JsonElement.DeepEquals(p.Value, other))), at + "/when", "CONFIG_CONFLICT: overlapping part mappings");
            previous.Add(when);
            Require(Text(entry.GetProperty("part")) is { Length: <= 100 }, at + "/part", "must be an exact EPLAN part number");
            Require(Integer(entry.GetProperty("variant"), 0), at + "/variant", "must be a nonnegative integer");
            Require(entry.GetProperty("quantity").ValueKind == JsonValueKind.Number && entry.GetProperty("quantity").GetDecimal() > 0, at + "/quantity", "must be a positive quantity");
            Source(entry.GetProperty("source"), at + "/source", resolve);
        });
        return new(spec.Clone(), schema);
    }

    public static SampleDefinition Sample(JsonElement spec, Func<string, PackResource?> resolve)
    {
        Object(spec, "/spec", "workflow inputs configFixture expectedChecks expectedObjects sources", "");
        FiniteNumbers(spec, "/spec");
        Reference(spec.GetProperty("workflow"), "workflow", "/spec/workflow", resolve);
        Reference(spec.GetProperty("configFixture"), "config_layer", "/spec/configFixture", resolve);
        Require(spec.GetProperty("inputs").ValueKind == JsonValueKind.Object, "/spec/inputs", "must contain workflow inputs; workflow schema validation is a separate gate");
        Each(spec.GetProperty("expectedChecks"), "/spec/expectedChecks", 1, 50, (v, p) => Reference(v, "rule", p, resolve));
        Each(spec.GetProperty("expectedObjects"), "/spec/expectedObjects", 1, 200, (v, p) => Require(Id(v), p, "must name a relative expected object"));
        Each(spec.GetProperty("sources"), "/spec/sources", 1, 20, (v, p) => Source(v, p, resolve));
        return new(spec.Clone());
    }

    private static JsonElement Parameters(JsonElement parameters, bool bindings)
    {
        Require(parameters.ValueKind == JsonValueKind.Object && parameters.EnumerateObject().Count() <= 50, "/spec/parameters", "must contain at most 50 named parameters");
        var items = new JsonObject();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty entry in parameters.EnumerateObject())
        {
            string at = Child("/spec/parameters", entry.Name);
            Object(entry.Value, at, bindings ? "type required binding" : "type required", "unit enum min max default");
            JsonObject item = JsonNode.Parse(entry.Value.GetRawText())!.AsObject();
            if (bindings)
            {
                JsonElement binding = entry.Value.GetProperty("binding");
                Object(binding, at + "/binding", "placeholder variable propertyId", "language");
                Require(Text(binding.GetProperty("placeholder")) is not null && Text(binding.GetProperty("variable")) is not null && Integer(binding.GetProperty("propertyId"), 1), at + "/binding", "requires a placeholder, variable and positive property id");
                Require(!binding.TryGetProperty("language", out JsonElement language) || Text(language) is not null, at + "/binding/language", "must name a language");
                Require(targets.Add(binding.GetProperty("placeholder").GetString() + "\n" + binding.GetProperty("variable").GetString()), at + "/binding", "a placeholder variable may bind only one parameter");
                item.Remove("binding");
            }
            item["allowedLayers"] = new JsonArray("base");
            items[entry.Name] = item;
        }
        JsonElement schema = JsonSerializer.SerializeToElement(new JsonObject { ["items"] = items }, PackJson.Options);
        try
        {
            ConfigValidator.Schema(schema);
        }
        catch (PackFormatException ex)
        {
            throw new PackFormatException(ex.Message.Replace("/spec/items", "/spec/parameters", StringComparison.Ordinal), ex);
        }
        return schema;
    }

    internal static void Values(JsonElement schema, JsonElement values, string at, bool partial = false)
    {
        Require(values.ValueKind == JsonValueKind.Object, at, "must contain typed parameter values");
        FiniteNumbers(values, at);
        JsonElement items = schema.GetProperty("items");
        foreach (JsonProperty entry in values.EnumerateObject())
        {
            string pointer = Child(at, entry.Name);
            Require(items.TryGetProperty(entry.Name, out JsonElement item), pointer, "must name a declared parameter");
            ConfigValidator.Value(item, entry.Value, pointer);
        }
        foreach (JsonProperty item in items.EnumerateObject())
        {
            Require(partial || !item.Value.GetProperty("required").GetBoolean() || item.Value.TryGetProperty("default", out _) || values.TryGetProperty(item.Name, out _), Child(at, item.Name), "required parameter has no value or default");
        }
    }

    private static RuleNode Predicate(JsonElement predicate, string at, JsonElement schema, Func<string, RuleValueType?>? config)
    {
        JsonElement rule = JsonSerializer.SerializeToElement(new { severity = "recommended", source = "module", scope = new { kind = "function", where = new { literal = true } }, check = predicate, message = new { en = "Module applicability" } }, PackJson.Options);
        try
        {
            return RuleValidator.Validate(rule, config, name => schema.GetProperty("items").TryGetProperty(name, out JsonElement item) ? new RuleValueType(item.GetProperty("type").GetString() == "choice" ? "text" : item.GetProperty("type").GetString()!, item.TryGetProperty("unit", out JsonElement unit) ? unit.GetString() : null) : null).Check;
        }
        catch (PackFormatException ex)
        {
            throw new PackFormatException(ex.Message.Replace("/spec/check", at, StringComparison.Ordinal), ex);
        }
    }

    private static void Source(JsonElement source, string at, Func<string, PackResource?> resolve)
    {
        Object(source, at, "asset hash location", "");
        PackResource? resource = Text(source.GetProperty("asset")) is { } id ? resolve(id) : null;
        Require(resource is not null && resource.Kind is ResourceKind.Document or ResourceKind.Project or ResourceKind.ProjectBackup or ResourceKind.BasicProject or ResourceKind.Macro or ResourceKind.FactSet, at + "/asset", "must resolve to a source file or extracted facts");
        Require(Text(source.GetProperty("hash")) == resource!.Sha256, at + "/hash", "must match the source resource SHA-256");
        Require(Locator(source.GetProperty("location")), at + "/location", "must name source evidence without a machine path");
    }

    private static PackResource Reference(JsonElement node, string kind, string at, Func<string, PackResource?> resolve)
    {
        PackResource? resource = Text(node) is { } id ? resolve(id) : null;
        Require(resource is not null && JsonNamingPolicy.SnakeCaseLower.ConvertName(resource.Kind.ToString()) == kind, at, "must reference a " + kind + " resource in the locked closure");
        return resource!;
    }

    private static void Each(JsonElement array, string at, int min, int max, Action<JsonElement, string> check)
    {
        Require(array.ValueKind == JsonValueKind.Array && array.GetArrayLength() >= min && array.GetArrayLength() <= max, at, $"must contain {min} to {max} entries");
        foreach ((JsonElement value, int index) in array.EnumerateArray().Select((v, i) => (v, i)))
        {
            check(value, $"{at}/{index}");
        }
    }

    private static string? Text(JsonElement node) => node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(node.GetString()) && node.GetString()!.Length <= 2000 && !node.GetString()!.Any(char.IsControl) ? node.GetString() : null;
    private static bool Id(JsonElement node) => Text(node) is { } id && PackManifestValidator.IsId(id);
    private static bool Locator(JsonElement node) => Text(node) is { } text && !text.Contains(':', StringComparison.Ordinal) && !text.Contains('\\', StringComparison.Ordinal);
    private static bool Integer(JsonElement node, int min) => node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out int n) && n >= min;
    private static bool Scalar(JsonElement node) => node.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
}
