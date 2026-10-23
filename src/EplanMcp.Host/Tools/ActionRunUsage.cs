using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

internal static class ActionRunUsage
{
    public static CallToolResult For(string? topic)
    {
        if (topic is null || ToolUsageCatalog.Topics.Contains(topic, StringComparer.Ordinal))
        {
            var sections = ToolUsageCatalog.Usage[ActionRunTools.Tool];
            return ToolResults.Success("Whitelisted action usage.", new ToolUsageResult { Tool = ActionRunTools.Tool, Topic = topic, Text = string.Join("\n\n", topic is null ? sections.Values : [sections[topic]]) });
        }
        if (!ActionRunCatalog.Entries.TryGetValue(topic, out JsonObject? rule))
        { return ToolResults.Error(ActionRunCatalog.Denied().Error); }
        return ToolResults.Success("Whitelisted entry usage.", new ToolUsageResult
        {
            Tool = ActionRunTools.Tool,
            Topic = topic,
            Text = topic + "\nTier: " + ActionRunCatalog.Text(rule, "tier") + "; scope: " + ActionRunCatalog.Text(rule, "scope") + "; seconds: " + rule["seconds"] +
                "\nVariable parameters (string values; each name exactly once):\n| Name | Required | Allowed value |\n|---|---|---|\n" + string.Join("\n", rule["parameters"]!.AsObject().Select(p =>
                    "| " + (p.Key.EndsWith('1') ? p.Key[..^1] : p.Key.TrimEnd('?')) + " | " + (p.Key.EndsWith('?') ? "no" : "yes") + " | " + p.Value!.GetValue<string>().Replace("|", " / ", StringComparison.Ordinal) + " |")) +
                "\nFixed: " + rule["fixed"]!.ToJsonString() + "; project paths and destinations are supplied by the server.\n" + ActionRunCatalog.Text(rule, "effects") +
                (rule["extension"] is null ? "" : "\nOutput: exactly one non-empty " + rule["extension"] + " file; backup extension follows MDTYPE, PLC extension follows CONVERTERID.") +
                "\nExample: " + Example(rule).ToJsonString() + "\n" + ToolUsageCatalog.Usage[ActionRunTools.Tool][ToolUsageCatalog.Fields]
        });
    }
    internal static JsonObject Example(JsonObject rule)
    {
        var fields = new JsonObject();
        foreach ((string key, JsonNode? kind) in rule["parameters"]!.AsObject().Where(p => !p.Key.EndsWith('?')))
        {
            string value = kind!.GetValue<string>().Split('|')[0];
            fields[key.EndsWith('1') ? key[..^1] : key] = key == "VISIBLE1" ? "0" : key == "LAYER1" ? "EPLAN100" : key == "PROPERTYVALUE1" ? RecordExample(rule)
                : value == "file" ? "export" + rule["extension"]!.GetValue<string>().Split('|')[0] : value == "archive" ? "LibraryBackup" : value == "text" ? "Phoenix1" : value;
        }
        var example = new JsonObject { ["entry"] = ActionRunCatalog.Text(rule, "entry"), ["arguments"] = fields };
        if (ActionRunCatalog.Text(rule, "scope") == "project")
        { example["project"] = "Line1"; }
        return example;
    }
    private static string RecordExample(JsonObject rule) => rule["parameters"]!["PROPERTYID1"]!.GetValue<string>() switch
    {
        "22001" => "10 20 206",
        "22900" => "ABB",
        "22931" => "A-B.100-C_FS0_I_CH_DP",
        "22945" => "3RH2911-1FA22-0MA0",
        "22960" => "A-B.100-C_FS0_ACC",
        "22970" => "RIT.AX_Handle",
        _ => throw new InvalidOperationException("Missing verified record example."),
    };
}
