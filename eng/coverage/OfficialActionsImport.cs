using System.Text.Json.Nodes;

namespace EplanMcp.Coverage;

internal static class OfficialActionsImport
{
    internal const string RelativePath = "catalog/eplan-2026.0.3/actions.json";
    internal static string[] Import(string root, string folder)
    {
        string path = Path.Combine(root, RelativePath);
        var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        var messages = new List<string>();
        string? Read(string file)
        {
            try
            { return File.ReadAllText(Path.Combine(folder, file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { messages.Add("warning: Could not read " + file); return null; }
        }
        string? indexHtml = Read("_index.html");
        if (indexHtml is null)
        { return [.. messages, "warning: Index unavailable; catalog left unchanged."]; }
        Dictionary<string, string> index = OfficialActionPage.Index(indexHtml);
        if (index.Count == 0)
        { return ["warning: No action links recognized on index; catalog left unchanged."]; }
        string[] catalog = [.. rows.Select(r => r!["action"]!.GetValue<string>()).Distinct(StringComparer.OrdinalIgnoreCase)];
        messages.Add("Index actions without catalog rows: " + string.Join(", ", index.Keys.Except(catalog, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)));
        messages.Add("Catalog actions absent from index: " + string.Join(", ", catalog.Except(index.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)));
        foreach (var group in rows.GroupBy(r => r!["action"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase))
        {
            if (!index.TryGetValue(group.Key, out string? file) || Read(file) is not { } html)
            { continue; }
            OfficialActionPage page = OfficialActionPage.Parse(group.Key, html);
            if (page.Examples.Length == 0 && html.Contains("exampleSectionContent", StringComparison.OrdinalIgnoreCase))
            { messages.Add("warning: " + group.Key + " has no recognized example call."); }
            foreach (JsonNode? row in group)
            {
                string? type = row!["type"]?.GetValue<string>();
                foreach (JsonNode? parameter in row["parameters"]!.AsArray())
                {
                    string name = parameter!["name"]!.GetValue<string>();
                    if (!page.Parameters.TryGetValue(name, out string? description))
                    { messages.Add("warning: " + row["id"] + "/" + name + " has no parameter table entry."); continue; }
                    JsonObject facts = OfficialActionPage.Facts(description, type, name);
                    foreach (string key in new[] { "required", "type", "values", "default" })
                    { parameter.AsObject().Remove(key); if (facts[key] is { } value) { parameter[key] = value.DeepClone(); } }
                    if (description.Contains("default", StringComparison.OrdinalIgnoreCase) && facts["default"] is null)
                    { messages.Add("warning: " + row["id"] + "/" + name + " has no recognized literal default."); }
                }
                row.AsObject().Remove("examples");
                JsonObject[] examples = [.. page.Examples.Where(e => type is null || string.Equals(e.FirstOrDefault(p => p.Key.Equals("TYPE", StringComparison.OrdinalIgnoreCase)).Value?.GetValue<string>(), type, StringComparison.OrdinalIgnoreCase))];
                if (examples.Length > 0)
                { row["examples"] = new JsonArray([.. examples.Select(e => e.DeepClone())]); }
            }
        }
        File.WriteAllText(path, Format(rows));
        JsonNode[] parameters = [.. rows.SelectMany(r => r!["parameters"]!.AsArray()).OfType<JsonNode>()];
        messages.Add($"Imported {parameters.Count(p => p["values"] is not null)} parameters with values, {parameters.Count(p => p["default"] is not null)} with literal defaults; {rows.Count(r => r!["examples"] is not null)} rows with {rows.Sum(r => r!["examples"]?.AsArray().Count ?? 0)} examples.");
        return [.. messages];
    }

    private static string Compact(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(", ", obj.Select(p => Compact(JsonValue.Create(p.Key)) + ": " + Compact(p.Value))) + "}",
        JsonArray array => "[" + string.Join(", ", array.Select(Compact)) + "]",
        _ => node?.ToJsonString(CoverageJson.Options) ?? "null",
    };
    // Match the catalog's compact parameter rows and scalar arrays; avoid unrelated reformatting on import.
    private static string Format(JsonArray rows) => "[\n" + string.Join(",\n", rows.OfType<JsonObject>().Select(row => "  {\n"
        + string.Join(",\n", row.Select(p => "    " + Compact(JsonValue.Create(p.Key)) + ": " + (p.Key == "parameters"
            ? "[\n" + string.Join(",\n", p.Value!.AsArray().Select(v => "      " + Compact(v))) + "\n    ]"
            : p.Key == "examples" ? p.Value!.ToJsonString(CoverageJson.Options).Replace("\n", "\n    ", StringComparison.Ordinal) : Compact(p.Value)))) + "\n  }")) + "\n]\n";
}
