using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tools;

internal static class PageReviewRuleIssues
{
    internal static void Add(JsonArray issues, string plugin, ReviewRule rule, IReadOnlyList<RuleResult> results, PageReviewFacts facts, string page)
    {
        bool mandatory = rule.Spec["severity"]?.GetValue<string>() == "mandatory";
        string message = rule.Spec["message"]?["zh"]?.GetValue<string>() ?? rule.Spec["message"]?["en"]?.GetValue<string>() ?? rule.Definition.Rule;
        foreach (RuleResult result in results.Where(r => r.Status == RuleStatus.Fail))
        { Issue([result], "rule:", mandatory ? "error" : "warning", message); }
        RuleResult[] unknown = results.Where(r => r.Status == RuleStatus.Unknown).ToArray();
        if (unknown.Length > 0)
        {
            string[] missing = unknown.SelectMany(r => r.Evidence.Where(e => e.Value is null).Select(e => Missing(e.Location))).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            Issue(unknown, "rule_unknown:", mandatory ? "warning" : "info", string.Create(CultureInfo.InvariantCulture,
                $"{unknown.Length} objects have missing or unreadable values: {string.Join(", ", missing)}. {message}"));
        }

        string? Missing(string location)
        {
            JsonNode? node = rule.Spec;
            foreach (string part in location.Split('/').Skip(2))
            {
                if (node is JsonObject obj)
                { node = obj[part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)]; }
                else if (node is JsonArray array && int.TryParse(part, CultureInfo.InvariantCulture, out int index) && index < array.Count)
                { node = array[index]; }
                else
                { node = null; break; }
            }
            return node is JsonValue value && value.TryGetValue(out string? name) ? name : null;
        }

        // The rule message names the required value; these are the tools an engineer's change goes through.
        JsonObject Fix(JsonNode? locator, JsonObject? row) => rule.Definition.Kind == "connection"
            ? new JsonObject { ["tool"] = "eplan_connection_create", ["args"] = new JsonObject { ["project"] = locator?["project"]?.DeepClone(), ["page"] = page, ["hint"] = "Add an item {kind:\"definition_point\", position on this wire, color: the value the message requires}; a wrong potential is changed at its potential definition." } }
            : new JsonObject { ["tool"] = "eplan_device_update", ["args"] = new JsonObject { ["project"] = locator?["project"]?.DeepClone(), ["tag"] = row?["tag"]?.DeepClone(), ["hint"] = "Give new_tag with the letter the message requires." } };

        void Issue(IReadOnlyList<RuleResult> rows, string prefix, string severity, string text)
        {
            JsonObject? first = facts.GetObject(rows[0].Locator);
            JsonNode? locator = first?["locator"];
            issues.Add(new JsonObject
            {
                ["code"] = prefix + plugin + "/" + rule.Definition.Rule,
                ["severity"] = severity,
                ["page"] = page,
                ["position"] = first?["position"]?.DeepClone(),
                ["objects"] = new JsonArray(rows.Take(20).Select(r => facts.GetObject(r.Locator)?["locator"]?.DeepClone()).OfType<JsonNode>().ToArray()),
                ["message"] = text,
                ["reference"] = rule.Spec["source"]?.DeepClone(),
                ["fix"] = Fix(locator, first),
            });
        }
    }
}
