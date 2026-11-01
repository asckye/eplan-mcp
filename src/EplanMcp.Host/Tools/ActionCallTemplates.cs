using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

internal static class ActionCallTemplates
{
    internal static JsonObject For(ActionCatalogItem row)
    {
        var parameters = new JsonObject();
        var example = new JsonObject();
        foreach (JsonElement parameter in row.Parameters)
        {
            string name = parameter.GetProperty("name").GetString()!;
            bool denied = !ActionTryPolicy.Name(name) || name.Equals("TYPE", StringComparison.OrdinalIgnoreCase) || ActionTryPolicy.Denied("parameter", name);
            JsonNode? values = parameter.TryGetProperty("values", out JsonElement allowed) ? JsonNode.Parse(allowed.GetRawText()) : null;
            parameters[name] = new JsonObject
            {
                ["kind"] = "string/number/bool",
                ["required"] = parameter.TryGetProperty("required", out JsonElement required) ? JsonNode.Parse(required.GetRawText()) : null,
                ["allowed_values"] = values,
                ["denied"] = denied
            };
            if (!denied && parameter.TryGetProperty("required", out required) && required.ValueKind == JsonValueKind.True)
            { example[name] = values is JsonArray { Count: > 0 } list ? list[0]?.DeepClone() : JsonValue.Create("<simple value>"); }
        }
        bool blocked = ActionTryPolicy.Denied("action", row.Name + "/" + row.Type) || row.ExcludedReason is not null || row.WhitelistEntry is not null;
        return new()
        {
            ["tool"] = ActionTryTools.Tool,
            ["denied"] = blocked,
            ["exclusion_reason"] = row.ExcludedReason ?? (row.WhitelistEntry is not null ? "Use verified eplan_action_run entry " + row.WhitelistEntry : blocked ? "Matched the action deny list." : null),
            ["arguments"] = new JsonObject
            {
                ["project"] = new JsonObject { ["kind"] = "string", ["required"] = true },
                ["action"] = new JsonObject { ["const"] = row.Name, ["required"] = true },
                ["type"] = new JsonObject { ["const"] = row.Type, ["required"] = row.Type is not null },
                ["parameters"] = parameters,
                ["dry_run"] = new JsonObject { ["kind"] = "bool", ["required"] = false, ["default"] = false }
            },
            ["selection"] = new JsonObject
            {
                ["required"] = null,
                ["recommended"] = row.Source == "ui" || row.Context?.Contains("selection", StringComparer.Ordinal) == true,
                ["source"] = "Catalog context; requiredness is not recorded.",
                ["tool"] = "eplan_selection_set",
                ["how"] = "Discover target locators with eplan_object_find, then pass the scratch project and chosen objects to eplan_selection_set using its input schema. If it is absent from tools/list, selection cannot be set through this server."
            },
            ["example"] = new JsonObject { ["project"] = "<scratch project>", ["action"] = row.Name, ["type"] = row.Type, ["parameters"] = example, ["dry_run"] = true },
            ["note"] = "Null required/allowed_values means not recorded. Parameters must be simple values without paths, quotes, control characters or target overrides. Full profile, actionTry:on and per-call approval required."
        };
    }

    internal static JsonObject? Closest(ActionCatalogData data, string action, string? type, JsonElement given) => data.Items
        .Where(r => r.Name.Equals(action, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Type == type ? 0 : 1)
        .ThenBy(r => ApiCallTemplates.Distance(new JsonObject(r.Parameters.Select(p => new KeyValuePair<string, JsonNode?>(p.GetProperty("name").GetString()!, null))), given))
        .Select(For).FirstOrDefault();
}
