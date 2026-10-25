using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>The property toolset's catalog-only Read tool; runtime definitions require the later Bridge adapters.</summary>
[McpServerToolType]
internal sealed class PropertySchemaTools
{
    public const string Tool = "eplan_property_schema";
    internal static readonly JsonSerializerOptions MetadataJson = new(HostJson.Options)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [McpServerTool(Name = Tool, Title = "Describe EPLAN properties", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Describes shipped property IDs and read/write restrictions without EPLAN. Unknown facts are null. Example: {\"object_type\":\"Function\",\"name_contains\":\"TEXT\"}.")]
    public static CallToolResult Schema(
        PropertySchemaPaging paging,
        string object_type,
        JsonElement? properties = null,
        string? name_contains = null,
        [AllowedValues(ToolArguments.Concise, ToolArguments.Detailed)] string response_format = ToolArguments.Concise,
        [Range(1, 200)] int limit = 50,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        bool detailed = ToolArguments.IsDetailed(response_format, violations);
        if (!PropertyCatalog.Entries.Any(e => e.ObjectType == object_type))
        {
            violations.Add("Use an exact object_type from the schema enum.");
        }

        if (limit is < 1 or > 200)
        {
            violations.Add("limit must be between 1 and 200.");
        }

        if (name_contains is not null && (string.IsNullOrWhiteSpace(name_contains) || name_contains.Length > 500))
        {
            violations.Add("name_contains must contain 1 to 500 characters.");
        }

        if (properties is not null && name_contains is not null)
        {
            violations.Add("Use either properties or name_contains, not both.");
        }

        List<PropertyDictionaryEntry> selected = properties is { } selectors
            ? Select(selectors, object_type, violations)
            : [.. PropertyCatalog.Entries.Where(e => e.ObjectType == object_type && (name_contains is null
                ? e.ReadRefusal is null : e.PropertyName.Contains(name_contains, StringComparison.OrdinalIgnoreCase)))];
        selected = [.. selected.OrderBy(e => e.PropertyId ?? int.MaxValue).ThenBy(e => e.PropertyName, StringComparer.Ordinal)];
        string query = PageCursor.Fingerprint(Tool, PropertyCatalog.CatalogVersion, object_type,
            properties?.GetRawText(), name_contains, response_format, limit.ToString(CultureInfo.InvariantCulture));
        int offset = paging.Offset(cursor, query, violations);
        if (cursor is not null && offset >= selected.Count)
        {
            violations.Add("cursor is outside this result; omit it to restart.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var items = new JsonArray();
        var result = new JsonObject
        {
            ["catalog_version"] = PropertyCatalog.CatalogVersion,
            ["object_type"] = object_type,
            ["items"] = items,
            ["total"] = selected.Count,
        };
        string summary = string.Empty;
        void Page()
        {
            bool more = offset + items.Count < selected.Count;
            result["cursor"] = more ? paging.Encode(offset + items.Count, query) : null;
            result["truncated"] = more;
            summary = string.Create(CultureInfo.InvariantCulture, $"Listed {items.Count} of {selected.Count} property definitions.")
                + (more ? " Pass cursor for the rest or narrow the filters." : string.Empty);
        }

        Page();
        foreach (PropertyDictionaryEntry entry in selected.Skip(offset).Take(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(Item(entry, detailed));
            Page();
            int budget = detailed ? ResultShaper.MaxTokenBudget : ResultShaper.DefaultTokenBudget;
            if (ResultShaper.ResultTokens(result.ToJsonString(HostJson.Options)) + ResultShaper.EstimateTokens(summary) > budget)
            {
                items.RemoveAt(items.Count - 1);
                Page();
                break;
            }
        }

        return ToolResults.Success(summary, result);
    }

    private static List<PropertyDictionaryEntry> Select(JsonElement selectors, string objectType, List<string> violations)
    {
        var selected = new List<PropertyDictionaryEntry>();
        if (selectors.ValueKind != JsonValueKind.Array || selectors.GetArrayLength() is < 1 or > 200)
        {
            violations.Add("properties must be an array of 1 to 200 selectors.");
            return selected;
        }

        foreach ((JsonElement selector, int position) in selectors.EnumerateArray().Select((value, i) => (value, i)))
        {
            string path = string.Create(CultureInfo.InvariantCulture, $"properties[{position}]");
            if (selector.ValueKind != JsonValueKind.Object)
            {
                violations.Add($"{path} must be an object with property_id or property_name.");
                continue;
            }

            bool hasId = selector.TryGetProperty("property_id", out JsonElement id);
            bool hasName = selector.TryGetProperty("property_name", out JsonElement name);
            if (hasId == hasName || selector.EnumerateObject().Any(p => p.Name is not ("property_id" or "property_name" or "index"))
                || selector.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != selector.EnumerateObject().Count())
            {
                violations.Add($"{path} needs exactly one property_id or property_name, and optional index only, without duplicate fields.");
            }

            if (selector.TryGetProperty("index", out JsonElement index) && (index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out _)))
            {
                violations.Add($"{path}.index must be an int32; catalog metadata cannot validate instance index bounds.");
            }

            PropertyDictionaryEntry? entry = null;
            if (hasId && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int number) && number > 0)
            {
                entry = PropertyCatalog.Find(objectType, number);
            }
            else if (hasName && name.ValueKind == JsonValueKind.String)
            {
                entry = PropertyCatalog.Find(objectType, name.GetString()!);
            }

            if (entry is null)
            {
                violations.Add($"{path}: use a known numeric ID or exact formal name on this object_type.");
            }
            else if (selected.Contains(entry))
            {
                violations.Add($"{path} duplicates a definition; select each property once.");
            }
            else
            {
                selected.Add(entry);
            }
        }

        return selected;
    }

    private static JsonObject Item(PropertyDictionaryEntry entry, bool detailed)
    {
        int major = int.Parse(PropertyCatalog.CatalogVersion.Split('.')[0], CultureInfo.InvariantCulture);
        bool allowed = PropertyWriteWhitelist.Entries.Keys.Any(k => k.EplanMajorVersion == major
            && k.ObjectType == entry.ObjectType && k.PropertyId == entry.PropertyId);
        var item = new JsonObject
        {
            ["property_id"] = entry.PropertyId,
            ["property_name"] = entry.PropertyName,
            ["read"] = entry.ReadRefusal ?? "readable",
            ["write"] = new JsonObject
            {
                ["write_allowed"] = allowed,
                ["api_writable"] = null,
                ["reason"] = allowed ? null : "not_in_shipped_whitelist",
            },
        };
        if (detailed)
        {
            item["obsolete"] = entry.Obsolete;
            item["definition"] = JsonSerializer.SerializeToNode(new PropertyDefinitionMetadata(), MetadataJson);
            item["source"] = "catalog/eplan-2026.0.3/data-dictionary.jsonl";
            item["version"] = PropertyCatalog.CatalogVersion;
            item["runtime_differences"] = null;
        }

        return item;
    }
}
