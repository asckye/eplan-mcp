using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

/// <summary>Maps sanitized Bridge rows; opaque values remain display-only (A42/A44).</summary>
internal static class PropertyGetRows
{
    private static readonly string[] _details = ["value_type", "read_only", "computed", "observed_at", "display", "texts", "unit", "format", "source", "original_length"];

    internal static JsonObject Map(PropertyReadRow row, int query, bool detailed)
    {
        var item = JsonSerializer.SerializeToNode(row, PropertySchemaTools.MetadataJson)!.AsObject();
        item.Remove("definition");
        item["query"] = query;
        PropertyTypedValue? value = row.Status == "value" && row.ValueType != "opaque" ? row.Value : null;
        object? primitive = value?.Text ?? (object?)value?.Boolean ?? value?.IntegerValue ?? value?.Number ?? (object?)value?.DateTime;
        item["value"] = JsonSerializer.SerializeToNode(primitive, HostJson.Options);
        item["reason"] = ResultShaper.CleanText(row.Reason);
        if (row.Status == "read_refused")
        {
            foreach (string key in _details.Concat(["language", "fallback"]))
            {
                item[key] = null;
            }
        }
        if (!detailed)
        {
            foreach (string key in _details.Where(k => k != "display" || row.ValueType != "opaque" || row.Status == "read_refused"))
            {
                item.Remove(key);
            }
        }

        JsonObject target = item["target"]!.AsObject();
        foreach (string key in target.Where(p => p.Value is null).Select(p => p.Key).ToArray())
        {
            target.Remove(key);
        }
        item["target"] = ContextBuilder.Clean(target);
        return item;
    }
}
