using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage;

/// <summary>Only committed metadata enters the offline Host index; no SDK loading or documentation extraction.</summary>
internal static class ApiIndexGenerator
{
    internal const string RelativePath = "src/EplanMcp.Host/Tools/ApiIndex.g.base64";
    private static readonly JsonSerializerOptions _json = new(CoverageJson.Options) { WriteIndented = false };

    internal static string Render(string root) => Encode(Data(root));
    internal static ApiIndexData Data(string root)
    {
        CoverageReport report = CoverageReport.Build(root);
        ToolMapping mapping = ToolMapping.Build(root);
        string directory = Path.Combine(root, report.Catalog);
        ApiCatalog catalog = ApiCatalog.Load(directory);
        if (!report.SdkCatalog)
        {
            return new([], new(StringComparer.Ordinal));
        }
        JsonElement[] members = [.. Directory.GetFiles(Path.Combine(directory, "members"), "*.jsonl").Order(StringComparer.Ordinal)
            .SelectMany(ApiCatalog.Lines).Where(m => Text(m, "layer") == "operation")];
        JsonElement[] types = [.. ApiCatalog.Lines(Path.Combine(directory, "types.jsonl"))];
        JsonElement summary = CoverageJson.Read<JsonElement>(Path.Combine(directory, "summary.json"));
        var anchors = File.ReadLines(Path.Combine(root, "docs/eplan/api-notes.md")).Where(l => Regex.IsMatch(l, @"^### [AB]\d+\."))
            .ToDictionary(l => l[4..l.IndexOf('.', StringComparison.Ordinal)], l => "docs/eplan/api-notes.md#"
                + Regex.Replace(l[4..].ToLowerInvariant(), @"[^\p{L}\p{N}_ -]", "").Replace(' ', '-'), StringComparer.Ordinal);
        return Build(catalog, report, mapping, members, types, summary.GetProperty("layers").GetProperty("data_dictionary").GetProperty("byObject"), anchors);
    }

    internal static ApiIndexData Build(ApiCatalog catalog, CoverageReport report, ToolMapping mapping,
        JsonElement[] members, JsonElement[] types, JsonElement objects, IReadOnlyDictionary<string, string>? anchors = null)
    {
        var used = report.Units.ToDictionary(u => u.Unit, StringComparer.Ordinal);
        var groups = members.ToLookup(m => Text(m, "group") ?? Text(m, "id")!, StringComparer.Ordinal);
        var reaching = mapping.Tools.SelectMany(t => t.Api.Select(id => (Key: catalog.Resolve(id), t.Name))
            .Concat(t.DataDictionary.Select(key => (Key: (string?)key, t.Name)))).ToLookup(p => p.Key, p => p.Name);
        var entries = new List<ApiEntry>();
        foreach (Unit unit in catalog.Units.Values.Where(u => u.Layer != ApiCatalog.Action).OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            ApiRule? rule = CoverageReport.Rule(unit, report.OverlayRules);
            UnitCoverage? coverage = used.GetValueOrDefault(unit.Key);
            string? reason = unit.Exclusion ?? (rule?.Status == "excluded" ? rule.Reason + ": " + rule.Note : null);
            string status = reason is not null ? "excluded" : coverage?.Status ?? (rule?.Status == "env_blocked" ? "env_blocked" : "registered");
            string[] tools = [.. reaching[unit.Key].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            var details = new List<ApiDetail>();
            var aliases = new List<string> { unit.Key };
            string type = unit.Type ?? string.Empty;
            string ns = unit.Scope;
            if (unit.Layer == ApiCatalog.Operation)
            {
                foreach (JsonElement member in groups[unit.Key].OrderBy(m => Text(m, "id"), StringComparer.Ordinal))
                {
                    string id = Text(member, "id")!;
                    ApiParameter[] parameters = member.TryGetProperty("params", out JsonElement p)
                        ? p.Deserialize<ApiParameter[]>(CoverageJson.Options)! : [];
                    string returns = Text(member, "returns") ?? "System.Void";
                    string signature = id[2..].Split('(')[0] + "(" + string.Join(", ", parameters.Select(p => p.Type + " " + p.Name)) + ") : " + returns;
                    details.Add(new("overload", id, signature, Text(member, "summary"), parameters, returns,
                        Text(member, "accessors"), member.TryGetProperty("static", out JsonElement s) && s.GetBoolean(),
                        member.TryGetProperty("excluded", out JsonElement e) ? Text(e, "reason") + ": " + Text(e, "rule") : null,
                        Reference: report.Catalog + "/members/" + unit.Scope + ".jsonl", SetterAccess: Text(member, "setterAccess"), Access: Text(member, "access")));
                    aliases.Add(id);
                }
            }
            else
            {
                JsonElement obj = objects.GetProperty(unit.Scope);
                type = Text(obj, "propertyList")!;
                aliases.AddRange([type, Text(obj, "enum")!]);
                string objectId = type.Replace("PropertyList", "", StringComparison.Ordinal);
                aliases.AddRange(types.Where(t => Text(t, "id") == objectId).Select(t => Text(t, "id")!));
                ns = types.First(t => Text(t, "id") == type).GetProperty("ns").GetString()!;
                details.Add(new("dictionary", type, PropertyCount: obj.GetProperty("properties").GetInt32(),
                    Reference: "docs/tools/eplan_property_schema.md", Note: "Use eplan_property_schema for IDs and restrictions; eplan_property_get reads project/page/function/connection targets only. Other object readers are listed in tools when implemented."));
            }
            if (rule is not null)
            {
                details.Add(new("evidence", rule.Match, Reference: rule.Source, Note: rule.Note ?? rule.Condition));
            }
            ApiDetail first = details[0];
            string shortSignature = first.Signature ?? unit.Scope;
            var row = new ApiRow(first.Id, unit.Key, unit.Kind ?? "data_dictionary", shortSignature.Length > 180 ? shortSignature[..177] + "..." : shortSignature,
                status, reason, tools, coverage?.Partial ?? false);
            entries.Add(new(row, ns, type, unit.Layer == ApiCatalog.Operation ? unit.Key[(unit.Key.LastIndexOf('.') + 1)..] : unit.Scope,
                first.Summary, [.. aliases], [.. details]));
        }
        return new([.. entries], mapping.Tools.ToDictionary(t => t.Name, t => Evidence(t, anchors), StringComparer.Ordinal), report.EplanApi);
    }

    private static ApiDetail[] Evidence(ToolEntry tool, IReadOnlyDictionary<string, string>? anchors) => [..
        new[] { tool.Evidence.Spec }.OfType<string>().Concat(tool.Evidence.ApiNotes.Select(a => anchors?.GetValueOrDefault(a) ?? "docs/eplan/api-notes.md#" + a.ToLowerInvariant()))
        .Concat(tool.Evidence.Tests.Concat(tool.Evidence.OperationTests).Select(t => t.File + "#" + t.Name))
        .Concat(tool.Live.Select(l => l.Source)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        .Select(r => new ApiDetail("evidence", tool.Name, Reference: r, Note: "Tool-level coverage evidence; not independent verification of every overload."))];

    internal static string Encode(ApiIndexData data)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, data, _json);
        }
        return Convert.ToBase64String(buffer.ToArray()) + "\n";
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
}
