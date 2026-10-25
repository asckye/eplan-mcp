using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tools;

/// <summary>Stable cleaned entry stream. Fragments concatenate to one JSON value, then parse once.</summary>
internal static class PlanEntries
{
    internal static JsonNode? Clean(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Clean(p.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Clean)]),
        JsonValue value when value.TryGetValue(out string? text) => JsonValue.Create(ResultShaper.CleanFullText(text)),
        _ => node?.DeepClone(),
    };

    internal static JsonArray Items(JsonNode? node) => node as JsonArray ?? node?["entries"] as JsonArray
        ?? node?["items"] as JsonArray ?? new JsonArray(node?.DeepClone());

    internal static JsonObject? Summary(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        JsonObject summary = node is JsonObject ? Clean(node)!.AsObject() : new JsonObject();
        summary.Remove("entries");
        summary.Remove("items");
        summary["total"] = Items(node).Count;
        summary["failed"] = Items(node).Count(v => v?["status"]?.GetValue<string>() == "failed");
        return summary;
    }

    internal static List<JsonObject> Build(PlanRecord record, bool detailed)
    {
        var entries = new List<JsonObject>();
        foreach (PlanStep step in record.Definition.Steps.OrderBy(s => s.Index))
        {
            JsonObject value = JsonSerializer.SerializeToNode(step, PlanJson.Options)!.AsObject();
            value["tier"] = step.Tier.ToString();
            value["result"] = JsonSerializer.SerializeToNode(record.State.Result?.Steps.SingleOrDefault(s => s.Index == step.Index), PlanJson.Options);
            if (detailed)
            {
                foreach (string key in new[] { "args", "readScope", "modifyScope", "preconditions", "expected", "plannedChanges", "result" })
                {
                    Add("step", step.Index, key, value[key]);
                    value.Remove(key);
                }
            }
            else
            {
                value = ContextBuilder.Node(new
                {
                    step.Index,
                    step.Op,
                    Tier = step.Tier.ToString(),
                    step.Effective,
                    step.DependsOn,
                    Targets = step.ModifyScope.Pages,
                    Changes = step.PlannedChanges.Length,
                    Result = record.State.Result?.Steps.SingleOrDefault(s => s.Index == step.Index),
                });
            }
            Add("step", step.Index, "summary", value);
        }
        foreach (string section in new[] { "validation", "diff" })
        {
            int ordinal = 0;
            JsonNode? node = JsonSerializer.SerializeToNode(section == "validation" ? record.State.Result?.Validation : record.State.Result?.Diff);
            foreach (JsonNode item in Items(node).OfType<JsonNode>().OrderBy(v => v["step"]?.GetValue<int>() ?? -1)
                .ThenBy(v => section == "diff" ? v["class"]?.ToJsonString() : "", StringComparer.Ordinal)
                .ThenBy(v => v["locator"]?.ToJsonString(HostJson.Options), StringComparer.Ordinal)
                .ThenBy(v => (v["check"] ?? v["property"])?.ToJsonString(), StringComparer.Ordinal))
            {
                if (detailed || item["status"]?.GetValue<string>() == "failed" || item["class"]?.GetValue<string>() == "outside_scope")
                {
                    Add(section, item["step"]?.GetValue<int>() ?? -1, ordinal.ToString(CultureInfo.InvariantCulture), item);
                }
                ordinal++;
            }
        }
        return entries;

        void Add(string section, int index, string key, JsonNode? value)
        {
            JsonNode? clean = Clean(value);
            string json = clean?.ToJsonString(HostJson.Options) ?? "null";
            List<string> fragments = [];
            for (int offset = 0; offset < json.Length;)
            {
                int length = Math.Min(256, json.Length - offset);
                if (char.IsHighSurrogate(json[offset + length - 1]))
                {
                    length--;
                }
                fragments.Add(json.Substring(offset, length));
                offset += length;
            }
            bool split = ResultShaper.EstimateTokens(json) > 512;
            for (int i = 0; i < (split ? fragments.Count : 1); i++)
            {
                JsonObject entry = ContextBuilder.Node(new
                {
                    section,
                    index,
                    key,
                });
                entry["value"] = split ? JsonValue.Create(fragments[i]) : clean;
                if (split)
                {
                    entry["fragmentIndex"] = i;
                    entry["fragmentCount"] = fragments.Count;
                }
                entries.Add(entry);
            }
        }
    }
}
