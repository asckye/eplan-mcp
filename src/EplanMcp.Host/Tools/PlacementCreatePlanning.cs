using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>Read-only preview. Display language and absolute text outlines must come from trusted readers, never arguments.</summary>
internal static class PlacementCreatePlanning
{
    internal static async Task<CallToolResult> PreviewAsync(IBridgeClient bridge, JsonElement input, string? displayLanguage = null,
        Func<JsonElement, double[]?>? measureText = null, CancellationToken cancellationToken = default)
    {
        List<string> problems = Validate(input);
        if (problems.Count > 0)
        { return Invalid(problems); }
        if (!input.TryGetProperty("dry_run", out JsonElement dry) || !dry.GetBoolean())
        { return Missing("placement.create.execute", "Execution is unavailable; use dry_run=true to preview."); }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectGetData project = await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet,
                new() { Project = input.GetProperty("project").GetString() }, cancellationToken).ConfigureAwait(false);
            var args = new PageGetArgs { Project = project.Project.Path, Page = input.GetProperty("page").GetString()! };
            PageGetData page = await bridge.InvokeAsync<PageGetArgs, PageGetData>(OperationNames.PageGet, args, cancellationToken).ConfigureAwait(false);
            if (page.Page.Type != "Circuit")
            { return Invalid(["/page: use a Circuit page from eplan_page_list."]); }
            args.Page = page.Page.Name;
            PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, args, cancellationToken).ConfigureAwait(false);
            return Plan(input, geometry, displayLanguage, measureText);
        }
        catch (BridgeCallException ex)
        {
            ex.Error.Execution = ExecutionState.NotStarted;
            return ToolResults.Error(ex.Error);
        }
    }

    internal static List<string> Validate(JsonElement input)
    {
        var problems = new List<string>();
        void Check(bool valid, string at, string message)
        { if (!valid) { problems.Add(at + ": " + message); } }
        bool Members(JsonElement node, string at, string required, string optional = "")
        {
            Check(node.ValueKind == JsonValueKind.Object, at, "must be an object.");
            if (node.ValueKind != JsonValueKind.Object)
            { return false; }
            foreach (string name in required.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            { Check(node.TryGetProperty(name, out _), at + "/" + name, "is required."); }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty p in node.EnumerateObject())
            { Check(seen.Add(p.Name) && (required + " " + optional).Split(' ').Contains(p.Name, StringComparer.Ordinal), Child(at, p.Name), "unknown or duplicate member."); }
            return true;
        }
        void Number(JsonElement node, string at, double min, double max) => Check(node.ValueKind == JsonValueKind.Number
            && node.TryGetDecimal(out _) && node.TryGetDouble(out double n) && double.IsFinite(n) && n >= min && n <= max, at, "must be a finite number in range.");
        void Point(JsonElement node, string at)
        {
            Check(node.ValueKind == JsonValueKind.Array && node.GetArrayLength() == 2, at, "requires [x_mm,y_mm].");
            if (node.ValueKind == JsonValueKind.Array)
            { foreach ((JsonElement n, int i) in node.EnumerateArray().Select((n, i) => (n, i))) { Number(n, at + "/" + i.ToString(CultureInfo.InvariantCulture), 0, 10000); } }
        }
        if (!Members(input, "", "project page items", "snap_to_grid dry_run response_format"))
        { return problems; }
        Check(Encoding.UTF8.GetByteCount(input.GetRawText()) <= 256 * 1024, "", "request exceeds 256 KiB.");
        foreach (string name in new[] { "project", "page" })
        { Check(input.TryGetProperty(name, out JsonElement s) && s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()), "/" + name, "requires an explicit nonempty name."); }
        foreach (string name in new[] { "snap_to_grid", "dry_run" })
        { Check(!input.TryGetProperty(name, out JsonElement b) || b.ValueKind is JsonValueKind.True or JsonValueKind.False, "/" + name, "must be boolean."); }
        Check(!input.TryGetProperty("response_format", out JsonElement f) || f.ValueKind == JsonValueKind.String && f.GetString() is "concise" or "detailed", "/response_format", "use concise or detailed.");
        if (!input.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
        { problems.Add("/items: requires an array."); return problems; }
        Check(items.GetArrayLength() is >= 1 and <= 50, "/items", "requires 1..50 items.");
        foreach ((JsonElement item, int i) in items.EnumerateArray().Select((item, i) => (item, i)))
        {
            string at = "/items/" + i.ToString(CultureInfo.InvariantCulture);
            string? kind = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("kind", out JsonElement k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            if (!Members(item, at, kind == "text" ? "kind position contents height_mm" : kind == "line" ? "kind points" : "kind corners"))
            { continue; }
            Check(kind is "text" or "line" or "rectangle", at + "/kind", "use text, line or rectangle.");
            if (kind == "text")
            {
                if (item.TryGetProperty("position", out JsonElement p))
                { Point(p, at + "/position"); }
                if (item.TryGetProperty("height_mm", out JsonElement h))
                { Number(h, at + "/height_mm", 0.5, 100); }
                if (!item.TryGetProperty("contents", out JsonElement c) || c.ValueKind != JsonValueKind.Object)
                { problems.Add(at + "/contents: requires explicit language contents."); continue; }
                var languages = new HashSet<string>(StringComparer.Ordinal);
                int length = 0;
                foreach (JsonProperty text in c.EnumerateObject())
                {
                    Check(languages.Add(text.Name) && text.Name.Length == 5 && Regex.IsMatch(text.Name, "^([a-z]{2}_[A-Z]{2}|\\?\\?_\\?\\?)$", RegexOptions.CultureInvariant), Child(at + "/contents", text.Name), "requires a unique language code, e.g. en_US or ??_??.");
                    Check(text.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.Value.GetString()), Child(at + "/contents", text.Name), "requires nonempty text.");
                    length += text.Value.ValueKind == JsonValueKind.String ? text.Value.GetString()!.Length : 0;
                }
                Check(length is >= 1 and <= 500, at + "/contents", "requires 1..500 characters in total.");
            }
            else if (item.TryGetProperty(kind == "line" ? "points" : "corners", out JsonElement pair))
            {
                string path = at + (kind == "line" ? "/points" : "/corners");
                Check(pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2, path, "requires exactly two points.");
                if (pair.ValueKind == JsonValueKind.Array)
                { foreach ((JsonElement p, int j) in pair.EnumerateArray().Select((p, j) => (p, j))) { Point(p, path + "/" + j.ToString(CultureInfo.InvariantCulture)); } }
            }
        }
        return problems;
    }

    private static CallToolResult Plan(JsonElement input, PageDrawingGeometry page, string? displayLanguage, Func<JsonElement, double[]?>? measureText)
    {
        if (page.PageSize is not { Length: 2 } size || size.Any(n => !double.IsFinite(n) || n <= 0) || !double.IsFinite(page.GridSize) || page.GridSize <= 0)
        { return Missing("placement.page_geometry", "Reliable Page.Size and grid are required; plot-frame size is not a fallback."); }
        bool snap = !input.TryGetProperty("snap_to_grid", out JsonElement s) || s.GetBoolean();
        var rows = new JsonArray();
        var changes = new List<string>();
        var problems = new List<string>();
        foreach ((JsonElement item, int i) in input.GetProperty("items").EnumerateArray().Select((item, i) => (item, i)))
        {
            string at = "/items/" + i.ToString(CultureInfo.InvariantCulture);
            string kind = item.GetProperty("kind").GetString()!;
            JsonNode actual = JsonNode.Parse(item.GetRawText())!;
            string field = kind == "text" ? "position" : kind == "line" ? "points" : "corners";
            JsonArray[] points = kind == "text" ? [actual[field]!.AsArray()] : [.. actual[field]!.AsArray().Select(p => p!.AsArray())];
            foreach (JsonArray point in points)
            { for (int j = 0; j < 2; j++) { double n = point[j]!.GetValue<double>(); point[j] = snap ? Math.Round(n / page.GridSize, MidpointRounding.AwayFromZero) * page.GridSize : n; } }
            if (points.SelectMany(p => p).Any(n => !double.IsFinite(n!.GetValue<double>()) || n.GetValue<double>() is < 0 or > 10000))
            { problems.Add(at + ": snapping must produce finite coordinates in 0..10000 mm."); continue; }
            if (kind == "text" && !item.GetProperty("contents").TryGetProperty("??_??", out _))
            {
                if (displayLanguage is null)
                { return Missing("placement.display_language", "Read the project's display language before planning language-specific text (A54)."); }
                if (!item.GetProperty("contents").TryGetProperty(displayLanguage, out _))
                { problems.Add(at + "/contents: include ??_?? or the project display language " + displayLanguage + " (A54)."); continue; }
            }
            double[]? bounds = kind == "text" ? measureText?.Invoke(JsonSerializer.SerializeToElement(actual)) :
                [points.Min(p => p[0]!.GetValue<double>()), points.Min(p => p[1]!.GetValue<double>()), points.Max(p => p[0]!.GetValue<double>()), points.Max(p => p[1]!.GetValue<double>())];
            if (bounds is not { Length: 4 } || bounds.Any(n => !double.IsFinite(n)) || bounds[0] > bounds[2] || bounds[1] > bounds[3])
            { return Missing("placement.text_outline", "A reliable full text outline is required; character-count estimates are not supported."); }
            if (bounds[0] < 0 || bounds[1] < 0 || bounds[2] > size[0] || bounds[3] > size[1] || bounds.Any(n => n > 10000))
            { problems.Add(at + ": the full outline after snapping must be inside Page.Size and 0..10000 mm."); }
            rows.Add(new JsonObject { ["requested"] = JsonNode.Parse(item.GetRawText()), ["actual"] = actual, ["bounds"] = JsonSerializer.SerializeToNode(bounds), ["snapped"] = !JsonElement.DeepEquals(item, JsonSerializer.SerializeToElement(actual)) });
            changes.Add($"create {kind} {ResultShaper.CleanFullText(page.Page)} item {i.ToString(CultureInfo.InvariantCulture)}: {actual.ToJsonString()} bounds {JsonSerializer.Serialize(bounds)}");
        }
        if (problems.Count > 0)
        { return Invalid(problems); }
        var result = new JsonObject { ["project"] = ResultShaper.CleanText(page.Project), ["page"] = ResultShaper.CleanText(page.Page), ["execution"] = ExecutionState.NotStarted, ["dryRun"] = true, ["gridSize"] = page.GridSize, ["boundarySource"] = "Page.Size", ["items"] = rows, ["changes"] = JsonSerializer.SerializeToNode(changes), ["connectionChanges"] = new JsonArray() };
        int budget = input.TryGetProperty("response_format", out JsonElement format) && format.GetString() == "detailed" ? 20000 : 5000;
        return ResultShaper.Fits(result, budget) ? ToolResults.Success("Dry run: would create text/graphics; no connection changes. Nothing was changed.", result)
            : Invalid(["/response_format: use detailed or fewer items; the complete impact list cannot be truncated."]);
    }

    private static string Child(string at, string name) => at + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static CallToolResult Invalid(IReadOnlyList<string> problems)
    {
        BridgeError error = ToolResults.InvalidArgumentError(problems);
        error.Execution = ExecutionState.NotStarted;
        return ToolResults.Error(error);
    }
    private static CallToolResult Missing(string missing, string message) => ToolResults.Error(new BridgeError
    { Reason = ErrorReason.CapabilityMissing, Execution = ExecutionState.NotStarted, Message = message, Metadata = new() { ["missing"] = missing } });
}
