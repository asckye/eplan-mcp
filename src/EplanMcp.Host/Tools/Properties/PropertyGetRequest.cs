using System.Globalization;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>Host batch validation and catalog resolution; target/selector shapes remain Bridge responsibilities.</summary>
internal static class PropertyGetRequest
{
    internal static CallToolResult? Validate(string originalJson, out Dictionary<(int Query, int Selector), int> properties)
    {
        properties = [];
        var violations = new List<PropertyViolation>();
        if (Encoding.UTF8.GetByteCount(originalJson) > 64 * 1024)
        {
            Add(violations, string.Empty, "Tool input exceeds 64 KiB of UTF-8 JSON.");
        }
        using JsonDocument document = JsonDocument.Parse(originalJson);
        JsonElement root = document.RootElement;
        Fields(root, ["project", "queries", "language", "response_format", "limit", "cursor"], string.Empty, violations);
        Duplicates(root, string.Empty, violations);
        foreach (string field in new[] { "project", "language", "cursor", "response_format", "limit" })
        {
            JsonElement value = Get(root, field);
            bool invalid = value.ValueKind != JsonValueKind.Undefined && (field switch
            {
                "limit" => value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int limit) || limit is < 1 or > 200,
                "language" => Text(value) is not { Length: 5 } code || code[2] != '_' || code[..2].Any(c => c is < 'a' or > 'z')
                    || code[3..].Any(c => c is < 'A' or > 'Z'),
                "response_format" => Text(value) is not ("concise" or "detailed"),
                _ => string.IsNullOrWhiteSpace(Text(value)),
            });
            if (invalid)
            {
                Add(violations, "/" + field, "Use the published argument type, enum and bounds, or omit this optional argument.");
            }
        }

        JsonElement[] batch = Items(Get(root, "queries"), 50, "/queries", violations);
        var resolved = new Dictionary<(int Query, int Selector), int>();
        var keys = new HashSet<(string Target, int Id, int? Index)>();
        int count = 0;
        foreach ((JsonElement query, int i) in batch.Select((q, i) => (q, i)))
        {
            string at = "/queries/" + i.ToString(CultureInfo.InvariantCulture);
            Fields(query, ["target", "properties"], at, violations);
            JsonElement target = Get(query, "target");
            string type = ObjectType(target);
            JsonElement[] selectors = Items(Get(query, "properties"), 20, at + "/properties", violations);
            count += selectors.Length;
            foreach ((JsonElement selector, int j) in selectors.Select((p, j) => (p, j)))
            {
                JsonElement name = Get(selector, "property_name");
                JsonElement number = Get(selector, "property_id");
                bool named = name.ValueKind == JsonValueKind.String && number.ValueKind == JsonValueKind.Undefined;
                int? id = named ? PropertyCatalog.Find(type, name.GetString()!)?.PropertyId
                    : name.ValueKind == JsonValueKind.Undefined && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out int n) && n > 0 ? n : null;
                string path = at + "/properties/" + j.ToString(CultureInfo.InvariantCulture);
                if (named && type.Length > 0 && id is null)
                {
                    Add(violations, path + "/property_name", "Use a public ID or exact formal name on this target's dictionary type.");
                }
                else if (id is { } value)
                {
                    resolved[(i, j)] = value;
                    JsonElement index = Get(selector, "index");
                    if ((index.ValueKind == JsonValueKind.Undefined || index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out _))
                        && type.Length > 0 && TargetKey(target) is { } key && !keys.Add((key, value, index.ValueKind == JsonValueKind.Number ? index.GetInt32() : null)))
                    {
                        Add(violations, path, "Duplicate target, property ID, index and language.");
                    }
                }
            }
        }
        if (count > 200)
        {
            Add(violations, "/queries", "Queries exceed 200 property requests.");
        }
        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments([JsonSerializer.Serialize(violations, PropertySchemaTools.MetadataJson)]);
        }

        properties = resolved;
        return null;
    }

    private static string ObjectType(JsonElement target) => new string?[]
    {
        Get(target, "project_object").ValueKind != JsonValueKind.Undefined ? "Project" : null,
        Get(target, "device_tag").ValueKind != JsonValueKind.Undefined ? "Function" : null,
        Get(target, "page").ValueKind != JsonValueKind.Undefined && Get(target, "device_tag").ValueKind == JsonValueKind.Undefined ? "Page" : null,
        Get(target, "locator").ValueKind == JsonValueKind.Undefined ? null : Text(Get(Get(target, "locator"), "kind")) is { } kind
            && kind is "page" or "function" or "connection" ? char.ToUpperInvariant(kind[0]) + kind[1..] : string.Empty,
    }.OfType<string>().ToArray() is [string type] ? type : string.Empty;

    private static string? TargetKey(JsonElement target) => Get(target, "locator").ValueKind != JsonValueKind.Undefined
        && (string.IsNullOrWhiteSpace(Text(Get(Get(target, "locator"), "project"))) || !Positive(Get(Get(target, "locator"), "type"), int.MaxValue) || !Positive(Get(Get(target, "locator"), "oid")))
        ? null : JsonSerializer.Serialize<string?[]>([ObjectType(target), Text(Get(target, "device_tag")),
        Text(Get(target, "page")), Text(Get(Get(target, "locator"), "project")), Text(Get(Get(target, "locator"), "kind")),
        Get(Get(target, "locator"), "type").ToString(), Get(Get(target, "locator"), "oid").ToString()]);
    private static JsonElement Get(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out JsonElement value) ? value : default;
    private static string? Text(JsonElement node) => node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    private static bool Positive(JsonElement node, long maximum = long.MaxValue) => node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out long number) && number > 0 && number <= maximum;

    private static JsonElement[] Items(JsonElement node, int maximum, string path, List<PropertyViolation> violations)
    {
        JsonElement[] items = node.ValueKind == JsonValueKind.Array ? [.. node.EnumerateArray()] : [];
        if (items.Length < 1 || items.Length > maximum)
        {
            Add(violations, path, "Supply an array within the published request limits.");
        }
        return items;
    }

    private static void Fields(JsonElement node, string[] allowed, string path, List<PropertyViolation> violations)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            Add(violations, path, "Supply an object.");
            return;
        }
        foreach (JsonProperty field in node.EnumerateObject().Where(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
        {
            Add(violations, Child(path, field.Name), "Unknown fields are not allowed.");
        }
    }

    private static void Duplicates(JsonElement node, string path, List<PropertyViolation> violations)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty field in node.ValueKind == JsonValueKind.Object ? node.EnumerateObject() : [])
        {
            if (!names.Add(field.Name))
            {
                Add(violations, Child(path, field.Name), "Duplicate fields are not allowed.");
            }
            Duplicates(field.Value, Child(path, field.Name), violations);
        }
        foreach ((JsonElement item, int i) in (node.ValueKind == JsonValueKind.Array ? node.EnumerateArray() : []).Select((item, i) => (item, i)))
        {
            Duplicates(item, path + "/" + i.ToString(CultureInfo.InvariantCulture), violations);
        }
    }

    private static string Child(string path, string field) => path + "/" + field.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static void Add(List<PropertyViolation> violations, string path, string message) => violations.Add(new PropertyViolation
    {
        Path = path,
        Message = message,
    });
}
