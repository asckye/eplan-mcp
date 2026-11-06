using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class PageReviewTools
{
    private static readonly string[] _differenceKinds = ["missing", "extra", "mismatched"];
    private static readonly string[] _reviewFields = ["category", "main", "crossSection", "potentialName", "potentialType"];
    public const string Tool = "eplan_page_review";
    internal static readonly string[] Checks = ["dangling_ends", "unconnected_pins", "overlaps", "off_grid", "unintended_junctions", "interruption_pairing", "cable_cores", "eplan_messages", "expected", "rules"];
    [McpServerTool(Name = Tool, Title = "Review a drawn EPLAN page", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reviews wiring, geometry, targets and plugin rules. rule_packs selects installed plugins; omitted uses enabled rules. Read only. Coordinates: mm, bottom-left origin. See usage for standards and fixes. Example: {\"project\":\"Line1\",\"page\":\"=F+A1/1\"}.")]
    public static async Task<CallToolResult> ReviewAsync(IBridgeClient bridge, TimeProvider clock, string project, string page,
        string[]? checks = null, JsonElement? expected = null, [Range(0, 100)] double tolerance_mm = 0.5, bool include_image = false,
        [Range(1, 200)] int limit = 50, string? cursor = null, [AllowedValues("concise", "detailed")] string response_format = "concise",
        IProgress<ProgressNotificationValue>? progress = null, string[]? rule_packs = null, PackLibrary? packs = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(page))
        { errors.Add("/project and /page are required; use eplan_page_list."); }
        if (checks?.Any(c => !Checks.Contains(c, StringComparer.Ordinal)) == true)
        { errors.Add("/checks: Use only the schema enum values."); }
        if (rule_packs is not null && (rule_packs.Length is < 1 or > 10 || rule_packs.Any(string.IsNullOrWhiteSpace) || rule_packs.Distinct(StringComparer.Ordinal).Count() != rule_packs.Length))
        { errors.Add("/rule_packs: Use 1–10 unique installed plugin IDs."); }
        if (!double.IsFinite(tolerance_mm) || tolerance_mm is < 0 or > 100 || limit is < 1 or > 200 || response_format is not ("concise" or "detailed"))
        { errors.Add("/tolerance_mm, /limit or /response_format: Use schema bounds and enum."); }
        if (include_image)
        { errors.Add("/include_image: Inline images are deferred; use eplan_export_image with inline instead."); }
        JsonNode? target = expected is { } e ? JsonNode.Parse(e.GetRawText()) : null;
        if (expected is not null)
        { ExpectedReview.Validate(target, ExpectedReview.Schema(), "/expected", errors); }
        if (errors.Count > 0)
        { return ToolResults.InvalidArguments(errors); }
        try
        {
            PageReviewPlugins.Selection? selection = checks is null || checks.Contains("rules", StringComparer.Ordinal)
                ? await PageReviewPlugins.SelectAsync(bridge, packs, project, page, rule_packs, cancellationToken).ConfigureAwait(false) : null;
            DateTimeOffset started = clock.GetUtcNow();
            using ITimer timer = clock.CreateTimer(_ => { if (!cancellationToken.IsCancellationRequested) { progress?.Report(new() { Progress = (float)(clock.GetUtcNow() - started).TotalSeconds, Message = "Reviewing page" }); } }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
            JsonObject data = await bridge.InvokeAsync<object, JsonObject>(OperationNames.PageReview, new { project, page, checks, expected, tolerance_mm }, TimeSpan.FromSeconds(20), null, cancellationToken).ConfigureAwait(false);
            JsonObject actual = data["actual"]!.AsObject();
            actual["interruptions"] = new JsonArray(actual["interruptions"]!.AsArray().Where(p => p!["page"]!.GetValue<string>() == page).Select(p => p!.DeepClone()).ToArray());
            JsonArray issues = data["issues"]!.AsArray();
            if (selection is not null)
            { await PageReviewPlugins.ApplyAsync(data, selection, page, cancellationToken).ConfigureAwait(false); }
            if (target is JsonObject wanted && (checks is null || checks.Contains("expected", StringComparer.Ordinal)))
            {
                foreach (JsonNode? d in ExpectedReview.Compare(wanted, actual, page, tolerance_mm))
                { issues.Add(new JsonObject { ["code"] = "expected_" + d!["kind"], ["severity"] = "error", ["page"] = page, ["position"] = (d["target"]?["position"] ?? d["actual"]?["position"])?.DeepClone(), ["objects"] = new JsonArray(d["actual"]?["locator"] is { } located ? [located.DeepClone()] : []), ["message"] = "Target difference at " + d["path"], ["fix"] = new JsonObject { ["tool"] = "eplan_tool_usage", ["args"] = new JsonObject { ["tool"] = "eplan_placement_create" } }, ["difference"] = d.DeepClone() }); }
            }
            JsonNode[] all = issues.Select(i => i!).OrderBy(i => i["severity"]!.GetValue<string>() switch { "error" => 0, "warning" => 1, _ => 2 }).ToArray();
            int Count(string severity) => all.Count(i => i["severity"]!.GetValue<string>() == severity);
            int errorCount = Count("error"), warnings = Count("warning");
            data["summary"] = new JsonObject { ["errors"] = errorCount, ["warnings"] = warnings, ["infos"] = Count("info"), ["clean"] = errorCount + warnings == 0 };
            string fingerprint = PageCursor.Fingerprint(Tool, project, page, string.Join(",", checks ?? Checks), JsonSerializer.Serialize(rule_packs), target?.ToJsonString(), tolerance_mm.ToString("R", CultureInfo.InvariantCulture), response_format, data.ToJsonString());
            int offset = PageCursor.Offset(cursor, fingerprint, Tool, errors);
            if (cursor is not null && offset >= all.Length)
            { errors.Add("/cursor: Result changed; omit cursor and review again."); }
            if (errors.Count > 0)
            { return ToolResults.InvalidArguments(errors); }
            data.Remove("actual");
            data["issues"] = new JsonArray();
            data["total"] = all.Length;
            void Finish()
            {
                int next = offset + data["issues"]!.AsArray().Count;
                data["cursor"] = next < all.Length ? PageCursor.Encode(next, fingerprint) : null;
                data["truncated"] = next < all.Length;
                if (target is not null)
                { data["expected"] = new JsonObject(_differenceKinds.Select(k => KeyValuePair.Create(k, (JsonNode?)new JsonArray(data["issues"]!.AsArray().Where(i => i?["difference"]?["kind"]?.ToString() == k).Select(i => i!["difference"]!.DeepClone()).ToArray())))); }
            }
            int budget = response_format == "detailed" ? ResultShaper.MaxTokenBudget : ResultShaper.DefaultTokenBudget;
            Finish();
            if (ResultShaper.ResultTokens(data.ToJsonString()) > budget - 500)
            { return ToolResults.InvalidArguments(["/rule_packs: Plugin summaries exceed the response budget; select fewer rule_packs or use detailed."]); }
            foreach (JsonNode row in all.Skip(offset).Take(limit))
            {
                JsonNode issue = row.DeepClone();
                if (response_format == "detailed")
                { issue["readback"] = new JsonArray(actual.SelectMany(s => s.Value!.AsArray()).Where(r => r?["locator"] is { } locator && issue["objects"]!.AsArray().Any(l => JsonNode.DeepEquals(l, locator))).Select(r => Readback(r!)).ToArray()); }
                data["issues"]!.AsArray().Add(issue);
                Finish();
                if (ResultShaper.ResultTokens(data.ToJsonString()) > budget - 500)
                { data["issues"]!.AsArray().RemoveAt(data["issues"]!.AsArray().Count - 1); break; }
            }
            Finish();
            if (all.Length > offset && data["issues"]!.AsArray().Count == 0)
            { return ToolResults.InvalidArguments(["/response_format: A review issue exceeds the response budget; use detailed or select fewer checks and rule_packs."]); }
            string summary = errorCount + warnings == 0 ? page + ": clean" : string.Create(CultureInfo.InvariantCulture, $"{page}: {errorCount} errors, {warnings} warnings");
            return ToolResults.Success(ResultShaper.CleanText(summary) + string.Concat(data["issues"]!.AsArray().Take(5).Select(i => "\n" + i!["code"] + ": " + ResultShaper.CleanText(i["message"]?.ToString()))), ContextBuilder.Clean(data));
        }
        catch (BridgeCallException ex) { return ToolResults.Error(ex.Error); }
    }
    private static JsonNode Readback(JsonNode row)
    {
        JsonNode copy = row.DeepClone();
        foreach (string field in _reviewFields)
        { copy.AsObject().Remove(field); }
        return copy;
    }
    internal static void Configure(ModelContextProtocol.Protocol.Tool tool, JsonObject schema)
    {
        schema["properties"]!["expected"] = ExpectedReview.Schema();
        schema["properties"]!["checks"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Checks.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()) } };
        schema["properties"]!["rule_packs"] = JsonNode.Parse("""{"type":"array","minItems":1,"maxItems":10,"uniqueItems":true,"items":{"type":"string"}}""");
        tool.OutputSchema = JsonSerializer.SerializeToElement(JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","summary","issues","cursor","total","truncated"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"summary":{"type":"object","additionalProperties":false,"required":["errors","warnings","infos","clean"],"properties":{"errors":{"type":"integer","minimum":0},"warnings":{"type":"integer","minimum":0},"infos":{"type":"integer","minimum":0},"clean":{"type":"boolean"}}},"issues":{"type":"array","maxItems":200,"items":{"type":"object","additionalProperties":false,"required":["code","severity","page","position","objects","message","fix"],"properties":{"code":{"enum":["dangling_end","unconnected_pin","overlap","off_grid","unintended_junction","interruption_pairing","cable_core_segment","eplan_message","expected_missing","expected_extra","expected_mismatched"]},"severity":{"enum":["error","warning","info"]},"page":{"type":"string"},"position":{"type":["array","null"],"maxItems":2,"items":{"type":"number"}},"objects":{"type":"array","items":{"$ref":"#/$defs/locator"}},"message":{"type":"string"},"fix":{"type":"object","additionalProperties":false,"required":["tool","args"],"properties":{"tool":{"type":"string"},"args":{"type":"object"}}},"difference":{"$ref":"#/$defs/difference"},"readback":{"type":"array"}}}},"expected":{"type":"object","additionalProperties":false,"required":["missing","extra","mismatched"],"properties":{"missing":{"type":"array","items":{"$ref":"#/$defs/difference"}},"extra":{"type":"array","items":{"$ref":"#/$defs/difference"}},"mismatched":{"type":"array","items":{"$ref":"#/$defs/difference"}}}},"cursor":{"type":["string","null"]},"total":{"type":"integer","minimum":0},"truncated":{"type":"boolean"}},"$defs":{"locator":{"type":"object","additionalProperties":false,"required":["project","page","type","oid","digest"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"type":{"type":"integer"},"oid":{"type":"integer"},"digest":{"type":"string"}}},"difference":{"type":"object","additionalProperties":false,"required":["kind","path","target","actual"],"properties":{"kind":{"enum":["missing","extra","mismatched"]},"path":{"type":"string"},"target":{},"actual":{}}}}}"""));
        PageReviewRuleSchema.Configure(tool);
    }
}
