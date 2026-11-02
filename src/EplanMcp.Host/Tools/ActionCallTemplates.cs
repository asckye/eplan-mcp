using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

internal static class ActionCallTemplates
{
    internal static JsonObject For(ActionCatalogItem row)
    {
        var parameters = new JsonObject();
        JsonObject example = Example(row);
        foreach (JsonElement parameter in row.Parameters)
        {
            string name = parameter.GetProperty("name").GetString()!;
            bool denied = !ActionTryPolicy.Name(name) || name.Equals("TYPE", StringComparison.OrdinalIgnoreCase) || ActionTryPolicy.Denied("parameter", name);
            JsonNode? values = parameter.TryGetProperty("values", out JsonElement allowed) ? JsonNode.Parse(allowed.GetRawText()) : null;
            parameters[name] = new JsonObject
            {
                ["kind"] = parameter.TryGetProperty("type", out JsonElement kind) ? kind.GetString() : "string/number/bool",
                ["required"] = parameter.TryGetProperty("required", out JsonElement required) ? JsonNode.Parse(required.GetRawText()) : null,
                ["allowed_values"] = values,
                ["denied"] = denied
            };
            if (!example.Any(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) && !denied && parameter.TryGetProperty("required", out required) && required.ValueKind == JsonValueKind.True)
            { example[name] = values is JsonArray { Count: > 0 } list ? list[0]?.DeepClone() : JsonValue.Create("<simple value>"); }
        }
        bool contextBlocked = example.Any(p => !ActionTryPolicy.Name(p.Key) || ActionTryPolicy.Denied("parameter", p.Key) || !ActionTryPolicy.Value(p.Value?.GetValue<string>()));
        bool blocked = ActionTryPolicy.Denied("action", row.Name + "/" + row.Type) || row.ExcludedReason is not null || row.WhitelistEntry is not null || contextBlocked;
        return new()
        {
            ["tool"] = ActionTryTools.Tool,
            ["denied"] = blocked,
            ["exclusion_reason"] = row.ExcludedReason ?? (row.WhitelistEntry is not null ? "Use verified eplan_action_run entry " + row.WhitelistEntry : contextBlocked ? "The official example needs context or parameters excluded from trials." : blocked ? "Matched the action deny list." : null),
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
            ["note"] = "Null required/allowed_values means not recorded. Example paths/files are scratch imports/exports placeholders and remain subject to the deny list. Parameters must be simple values without paths, quotes, control characters or target overrides. Full profile, actionTry:on and per-call approval required."
        };
    }

    internal static JsonObject Example(ActionCatalogItem row)
    {
        // Official pages also show calls without parameters; prefer the first example that names the most required parameters.
        string[] required = [.. row.Parameters.Where(p => p.TryGetProperty("required", out JsonElement r) && r.ValueKind == JsonValueKind.True).Select(p => p.GetProperty("name").GetString()!)];
        JsonElement? chosen = row.Examples?.Select(e => (JsonElement?)e).OrderByDescending(e => required.Count(n => e!.Value.EnumerateObject().Any(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))).FirstOrDefault();
        var example = chosen is { } official ? JsonNode.Parse(official.GetRawText())!.AsObject() : new JsonObject();
        foreach (string name in example.Select(p => p.Key).ToArray())
        {
            if (name.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
            { example.Remove(name); continue; }
            JsonElement parameter = row.Parameters.FirstOrDefault(p => p.GetProperty("name").GetString()!.Equals(name, StringComparison.OrdinalIgnoreCase));
            string? kind = parameter.ValueKind == JsonValueKind.Object && parameter.TryGetProperty("type", out JsonElement type) ? type.GetString() : null;
            if (kind is "path" or "file" || Regex.IsMatch(example[name]?.GetValue<string>() ?? "", @"\A(?:[A-Za-z]:|\\\\)", RegexOptions.CultureInvariant))
            {
                string direction = name.Contains("IMPORT", StringComparison.OrdinalIgnoreCase) || name.Contains("SOURCE", StringComparison.OrdinalIgnoreCase) ? "imports"
                    : name.Contains("DESTINATION", StringComparison.OrdinalIgnoreCase) || name.Contains("EXPORT", StringComparison.OrdinalIgnoreCase) || name.Contains("OUTPUT", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("NewDatabase", StringComparison.OrdinalIgnoreCase) || name.Contains("ARCHIVE", StringComparison.OrdinalIgnoreCase) && row.Name.Equals("backup", StringComparison.OrdinalIgnoreCase)
                    || row.Type?.Contains("EXPORT", StringComparison.OrdinalIgnoreCase) == true
                    || row.Name.Contains("export", StringComparison.OrdinalIgnoreCase) ? "exports" : "imports";
                example[name] = name.Equals("PROJECTNAME", StringComparison.OrdinalIgnoreCase) || name.Equals("PROJECT", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("ProjectPath", StringComparison.OrdinalIgnoreCase) ? "<scratch project>" : "<scratch " + direction + "/" + name + ">";
            }
        }
        return example;
    }

    internal static JsonObject? Closest(ActionCatalogData data, string action, string? type, JsonElement given) => data.Items
        .Where(r => r.Name.Equals(action, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Type == type ? 0 : 1)
        .ThenBy(r => ApiCallTemplates.Distance(new JsonObject(r.Parameters.Select(p => new KeyValuePair<string, JsonNode?>(p.GetProperty("name").GetString()!, null))), given))
        .Select(For).FirstOrDefault();
}
