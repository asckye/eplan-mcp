using System.Text;
using System.Text.Json;
using EplanMcp.Packs.Scanning;
using static EplanMcp.Packs.BindingReadModel;

namespace EplanMcp.Packs;

/// <summary>Validates read evidence, never infers missing native relationships (A51/A56).</summary>
public static class ModuleInspectionReader
{
    public static ModuleInspection Read(JsonElement evidence, string hash, ModuleDefinition? module = null)
    {
        using JsonDocument document = PackJson.Parse(Encoding.UTF8.GetBytes(evidence.GetRawText()), "module.inspect");
        JsonElement root = document.RootElement;
        Object(root, "", "macroHash apiExtensionLicence complete coverage objects references placeholders gaps", "");
        FiniteNumbers(root, "");
        Require(Text(root.GetProperty("macroHash")) == hash && PackManifestValidator.IsSha256(hash), "/macroHash", "must match the inspected macro SHA-256");
        Require(root.GetProperty("apiExtensionLicence").ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null, "/apiExtensionLicence", "must be a confirmed boolean or unknown");
        Require(Boolean(root.GetProperty("complete")), "/complete", "must be boolean");
        var gaps = new List<ScanFinding>();
        void Gap(string at, string message) => gaps.Add(new(FindingSeverity.Gap, "module_incomplete", at, message));
        int details = 0;
        void Each(JsonElement array, string at, int max, Action<JsonElement, string> check)
        {
            Require(array.ValueKind == JsonValueKind.Array && array.GetArrayLength() <= max, at, "must be a bounded array");
            details += array.GetArrayLength();
            Require(details <= 10000, at, "inspection exceeds 10,000 details; reduce the scope without truncating the closure");
            foreach ((JsonElement item, int index) in array.EnumerateArray().Select((v, i) => (v, i)))
            { check(item, $"{at}/{index}"); }
        }
        if (root.GetProperty("complete").ValueKind != JsonValueKind.True)
        { Gap("/complete", "Native reference discovery is incomplete; inspect the gaps before generation."); }
        if (root.GetProperty("apiExtensionLicence").ValueKind != JsonValueKind.True)
        { Gap("/apiExtensionLicence", "A confirmed API Extension licence is required."); }
        JsonElement coverage = root.GetProperty("coverage");
        Object(coverage, "/coverage", "functions pages connections crossPageReferences placeholderBindings properties", "");
        foreach (JsonProperty field in coverage.EnumerateObject())
        {
            Require(Boolean(field.Value), "/coverage/" + field.Name, "must be boolean");
            if (field.Value.ValueKind != JsonValueKind.True)
            { Gap("/coverage/" + field.Name, "Native discovery does not cover this relationship or property set."); }
        }
        var objects = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Each(root.GetProperty("objects"), "/objects", 10000, (item, at) =>
        {
            Object(item, at, "id kind name page properties", "");
            Require(Locator(item.GetProperty("id")) && objects.TryAdd(item.GetProperty("id").GetString()!, item), at + "/id", "must be a unique stable locator without a machine path");
            Require(Text(item.GetProperty("kind")) is "function" or "page" or "connection" or "project" or "placeholder" or "graphic", at + "/kind", "must name a supported object kind");
            Require(Text(item.GetProperty("name")) is not null, at + "/name", "must name the object");
            Require(item.GetProperty("page").ValueKind == JsonValueKind.Null || Locator(item.GetProperty("page")), at + "/page", "must be a full page name or null for an unplaced object");
            Each(item.GetProperty("properties"), at + "/properties", 10000, (property, pointer) =>
            {
                Object(property, pointer, "propertyId index language state", "value");
                Require(Integer(property.GetProperty("propertyId"), 0) && Integer(property.GetProperty("index"), 0), pointer, "requires nonnegative property id and index");
                Require(property.GetProperty("language").ValueKind == JsonValueKind.Null || Text(property.GetProperty("language")) is not null, pointer + "/language", "must name a language or be null");
                string? state = Text(property.GetProperty("state"));
                Require(state is "value" or "empty" or "unavailable", pointer + "/state", "must be value, empty or unavailable");
                Require(state == "value" ? property.TryGetProperty("value", out JsonElement value) && Scalar(value) : !property.TryGetProperty("value", out _), pointer + "/value", "only value state carries a scalar value");
                if (state == "unavailable")
                { Gap(pointer, "The referenced property could not be read."); }
            });
        });
        Each(root.GetProperty("references"), "/references", 10000, (edge, at) =>
        {
            Object(edge, at, "from to kind", "");
            Require(Locator(edge.GetProperty("from")) && Locator(edge.GetProperty("to")), at, "requires stable endpoint locators");
            Require(Text(edge.GetProperty("kind")) is "connection" or "cross_reference" or "parent" or "subfunction" or "same_name" or "page", at + "/kind", "must name a supported relationship");
            if (!objects.ContainsKey(edge.GetProperty("from").GetString()!) || !objects.ContainsKey(edge.GetProperty("to").GetString()!))
            { Gap(at, "A reference endpoint is unresolved, possibly on another page; retain it as a gap."); }
        });
        foreach ((string id, JsonElement obj) in objects)
        {
            if (obj.GetProperty("page").GetString() is { } page && !objects.Values.Any(p => Text(p.GetProperty("kind")) == "page" && Text(p.GetProperty("name")) == page))
            { Gap("/objects", "An object's page is outside the discovered closure: " + id); }
        }
        Require(objects.Values.Count(o => Text(o.GetProperty("kind")) == "page") <= 50, "/objects", "reference closure exceeds 50 pages; reduce the scope without truncation");
        var bindings = new HashSet<(string Holder, string Variable, int Property, string? Language)>();
        var holders = new HashSet<string>(StringComparer.Ordinal);
        var placeholderObjects = new HashSet<string>(StringComparer.Ordinal);
        Each(root.GetProperty("placeholders"), "/placeholders", 200, (holder, at) =>
        {
            Object(holder, at, "object name variables valueSets bindings", "");
            Require(Locator(holder.GetProperty("object")) && Text(holder.GetProperty("name")) is not null, at, "requires a placeholder locator and name");
            Require(placeholderObjects.Add(holder.GetProperty("object").GetString()!), at + "/object", "must identify a distinct placeholder");
            if (!holders.Add(holder.GetProperty("name").GetString()!))
            { Gap(at + "/name", "The placeholder name is ambiguous."); }
            if (!objects.TryGetValue(holder.GetProperty("object").GetString()!, out JsonElement target) || Text(target.GetProperty("kind")) != "placeholder")
            { Gap(at + "/object", "The placeholder object is unresolved."); }
            var variables = new HashSet<string>(StringComparer.Ordinal);
            Each(holder.GetProperty("variables"), at + "/variables", 200, (v, p) => Require(Text(v) is { } name && variables.Add(name), p, "must be a unique variable name"));
            var sets = new HashSet<string>(StringComparer.Ordinal);
            Each(holder.GetProperty("valueSets"), at + "/valueSets", 200, (set, p) =>
            {
                Object(set, p, "name values", "");
                Require(Text(set.GetProperty("name")) is not null, p + "/name", "must name a value set");
                if (!sets.Add(set.GetProperty("name").GetString()!))
                { Gap(p + "/name", "The value-set name is ambiguous."); }
                JsonElement values = set.GetProperty("values");
                Require(values.ValueKind == JsonValueKind.Object && values.EnumerateObject().All(v => variables.Contains(v.Name)), p + "/values", "must contain only declared variables");
                foreach (string variable in variables)
                { if (!values.TryGetProperty(variable, out _)) { Gap(Child(p + "/values", variable), "The value set has no value for this variable."); } }
                foreach (JsonProperty value in values.EnumerateObject())
                {
                    Each(value.Value, Child(p + "/values", value.Name), 200, (text, pointer) =>
                    {
                        Object(text, pointer, "language value", "");
                        Require(text.GetProperty("language").ValueKind == JsonValueKind.Null || Text(text.GetProperty("language")) is not null, pointer + "/language", "must name a language or be null");
                        Require(Scalar(text.GetProperty("value")), pointer + "/value", "must be a scalar value");
                    });
                    if (value.Value.GetArrayLength() == 0)
                    { Gap(p + "/values", "The value set has no readable value for a variable."); }
                }
            });
            var mapped = new HashSet<string>(StringComparer.Ordinal);
            Each(holder.GetProperty("bindings"), at + "/bindings", 10000, (binding, p) =>
            {
                Object(binding, p, "variable target propertyId index language", "");
                Require(Text(binding.GetProperty("variable")) is { } v && variables.Contains(v) && Locator(binding.GetProperty("target")), p, "requires a declared variable and target locator");
                Require(Integer(binding.GetProperty("propertyId"), 0) && Integer(binding.GetProperty("index"), 0), p, "requires nonnegative property id and index");
                Require(binding.GetProperty("language").ValueKind == JsonValueKind.Null || Text(binding.GetProperty("language")) is not null, p + "/language", "must name a language or be null");
                string variable = binding.GetProperty("variable").GetString()!;
                int property = binding.GetProperty("propertyId").GetInt32();
                string? language = binding.GetProperty("language").GetString();
                mapped.Add(variable);
                bindings.Add((holder.GetProperty("name").GetString()!, variable, property, language));
                if (!objects.TryGetValue(binding.GetProperty("target").GetString()!, out JsonElement obj) || !obj.GetProperty("properties").EnumerateArray().Any(p => p.GetProperty("propertyId").GetInt32() == property && p.GetProperty("index").GetInt32() == binding.GetProperty("index").GetInt32() && p.GetProperty("language").GetString() == language))
                { Gap(p, "The bound object or actual property is unresolved."); }
            });
            foreach (string variable in variables.Except(mapped))
            { Gap(at + "/bindings", "A placeholder variable has no resolved property binding: " + variable); }
        });
        if (objects.Any(o => Text(o.Value.GetProperty("kind")) == "placeholder" && !placeholderObjects.Contains(o.Key)))
        { Gap("/placeholders", "A discovered placeholder has no inspected value sets or bindings."); }
        Each(root.GetProperty("gaps"), "/gaps", 10000, (gap, at) =>
        {
            Object(gap, at, "pointer message", "");
            Require(Text(gap.GetProperty("pointer")) is { } pointer && pointer.StartsWith('/') && Text(gap.GetProperty("message")) is not null, at, "requires a JSON Pointer and a hint");
            Gap(gap.GetProperty("pointer").GetString()!, gap.GetProperty("message").GetString()!);
        });
        if (module is not null)
        {
            Require(module.Spec.GetProperty("macro").GetProperty("hash").GetString() == hash, "/spec/macro/hash", "must match the inspected macro");
            foreach (JsonProperty parameter in module.Spec.GetProperty("parameters").EnumerateObject())
            {
                JsonElement b = parameter.Value.GetProperty("binding");
                if (!bindings.Contains((b.GetProperty("placeholder").GetString()!, b.GetProperty("variable").GetString()!, b.GetProperty("propertyId").GetInt32(), b.TryGetProperty("language", out JsonElement language) ? language.GetString() : null)))
                { Gap(Child("/spec/parameters", parameter.Name) + "/binding", "The declared binding is not proven by native inspection."); }
            }
        }
        return new(root.Clone(), module?.Spec.Clone(), gaps);
    }

    private static string? Text(JsonElement v) => v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
    private static bool Locator(JsonElement v) => Text(v) is { } text && !text.Contains(':', StringComparison.Ordinal) && !text.Contains('\\', StringComparison.Ordinal);
    private static bool Integer(JsonElement v, int min) => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) && n >= min;
    private static bool Boolean(JsonElement v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False;
    private static bool Scalar(JsonElement v) => v.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
}
