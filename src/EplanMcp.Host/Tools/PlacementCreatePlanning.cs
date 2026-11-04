using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>Read-only preview. Display language and absolute text outlines must come from trusted readers, never arguments.</summary>
internal static class PlacementCreatePlanning
{
    internal static readonly string[] AllowedPageTypes = ["Circuit", "Overview", "Graphics"];
    internal const string AllowedPageMessage = "/page requires a Circuit, Overview or Graphics page from eplan_page_list.";
    internal static async Task<CallToolResult> PreviewAsync(IBridgeClient bridge, JsonElement input, string? displayLanguage = null,
        Func<JsonElement, double[]?>? measureText = null, ImportFiles? files = null, CancellationToken cancellationToken = default)
    {
        List<string> problems = Validate(input);
        if (problems.Count > 0)
        { return Invalid(problems); }
        if (!WriteToolDefaults.IsPreview(input))
        { return Missing("placement.create.execute", "Execution is unavailable; use dry_run=true to preview."); }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectGetData project = await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet,
                new() { Project = input.GetProperty("project").GetString() }, cancellationToken).ConfigureAwait(false);
            var args = new PageGetArgs { Project = project.Project.Path, Page = input.GetProperty("page").GetString()! };
            PageGetData page = await bridge.InvokeAsync<PageGetArgs, PageGetData>(OperationNames.PageGet, args, cancellationToken).ConfigureAwait(false);
            if (!AllowedPageTypes.Contains(page.Page.Type, StringComparer.Ordinal))
            { return Invalid([AllowedPageMessage]); }
            args.Page = page.Page.Name;
            PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, args, cancellationToken).ConfigureAwait(false);
            string? layoutWarnings = null;
            if (measureText is null)
            {
                var preflight = new PlacementTextCreateArgs { Project = args.Project!, Page = args.Page, Items = [] };
                foreach (JsonElement item in input.GetProperty("items").EnumerateArray())
                {
                    JsonNode actual = Snap(item, geometry.GridSize, !input.TryGetProperty("snap_to_grid", out JsonElement s) || s.GetBoolean(), normalizeRectangle: false);
                    preflight.Items.Add(await RequestAsync(JsonSerializer.SerializeToElement(actual), files, cancellationToken).ConfigureAwait(false));
                }
                layoutWarnings = (await bridge.InvokeAsync<PlacementTextCreateArgs, PlacementTextCreateData>(OperationNames.PlacementTextCreatePlan, preflight, PlacementCreateTools.CallLimit, null, cancellationToken).ConfigureAwait(false)).Warning;
            }
            return Plan(input, geometry, displayLanguage, measureText, layoutWarnings);
        }
        catch (ImportFileException ex) { return ToolResults.Error(ex.Error); }
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
            && node.TryGetDouble(out double n) && double.IsFinite(n) && n >= min && n <= max, at, "must be a finite number in range.");
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
            if (!Members(item, at, kind == "path_text" ? "kind position contents" : IsText(kind) ? "kind position contents height_mm" : kind is "line" or "polyline" ? "kind points" : kind == "ellipse" ? "kind center radius_x_mm radius_y_mm" : kind == "arc" ? "kind center radius_mm start_angle end_angle" : kind == "image" ? "kind file corners" : "kind corners", "pen layer " + (kind == "path_text" ? "height_mm angle alignment" : kind == "text" ? "angle alignment" : kind is "ellipse" or "rectangle" ? "angle" : kind == "polyline" ? "closed" : "")))
            { continue; }
            Check(kind is "text" or "line" or "rectangle" or "polyline" or "arc" or "ellipse" or "path_text" or "image", at + "/kind", "unsupported primitive kind.");
            if (item.TryGetProperty("layer", out JsonElement layer))
            { Check(layer.ValueKind == JsonValueKind.Number && layer.TryGetInt32(out int id) && id >= 0, at + "/layer", "requires an integer layer ID."); }
            if (item.TryGetProperty("angle", out JsonElement rotation))
            { Number(rotation, at + "/angle", -double.MaxValue, double.MaxValue); }
            if (item.TryGetProperty("alignment", out JsonElement alignment))
            { Check(alignment.ValueKind == JsonValueKind.String && PlacementStyleSchema.Alignments.Contains(alignment.GetString(), StringComparer.Ordinal), at + "/alignment", "requires an EPLAN alignment name."); }
            if (item.TryGetProperty("closed", out JsonElement closed))
            { Check(closed.ValueKind is JsonValueKind.True or JsonValueKind.False, at + "/closed", "requires a boolean."); }
            if (item.TryGetProperty("pen", out JsonElement pen) && Members(pen, at + "/pen", "", "color width_mm style pattern_length line_end"))
            {
                foreach (JsonProperty p in pen.EnumerateObject())
                {
                    Number(p.Value, at + "/pen/" + p.Name, -double.MaxValue, double.MaxValue);
                    if (p.Name is "color" or "style" or "line_end")
                    { Check(p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int value) && (p.Name != "line_end" && value == -16002 || value >= 0 && value <= (p.Name == "color" ? 255 : p.Name == "style" ? 6 : 2)), at + "/pen/" + p.Name, "requires a supported integer value; line_end is 0..2."); }
                }
            }
            if (IsText(kind))
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
                Check(!languages.Contains("??_??") || languages.Count == 1, at + "/contents", "??_?? cannot be mixed with languages (A54/live T4b).");
            }
            else if (kind is "arc" or "ellipse")
            {
                if (item.TryGetProperty("center", out JsonElement center))
                { Point(center, at + "/center"); }
                foreach (string name in kind == "arc" ? new[] { "radius_mm", "start_angle", "end_angle" } : new[] { "radius_x_mm", "radius_y_mm", "angle" })
                { if (item.TryGetProperty(name, out JsonElement n)) { Number(n, at + "/" + name, name.StartsWith("radius", StringComparison.Ordinal) ? double.Epsilon : -double.MaxValue, name.StartsWith("radius", StringComparison.Ordinal) ? 10000 : double.MaxValue); } }
            }
            else if (item.TryGetProperty(kind is "line" or "polyline" ? "points" : "corners", out JsonElement pair))
            {
                string path = at + (kind is "line" or "polyline" ? "/points" : "/corners");
                Check(pair.ValueKind == JsonValueKind.Array && (kind == "polyline" ? pair.GetArrayLength() >= 2 : pair.GetArrayLength() == 2), path, kind == "polyline" ? "requires at least two points." : "requires exactly two points.");
                if (pair.ValueKind == JsonValueKind.Array)
                { foreach ((JsonElement p, int j) in pair.EnumerateArray().Select((p, j) => (p, j))) { Point(p, path + "/" + j.ToString(CultureInfo.InvariantCulture)); } }
            }
            if (kind == "image" && item.TryGetProperty("file", out JsonElement file))
            { Check(file.ValueKind == JsonValueKind.String && ImportFiles.Problem("file", file.GetString(), ImageExtensions) is null, at + "/file", "requires an image path below imports (.png/.jpg/.jpeg/.bmp/.emf/.wmf)."); }
        }
        return problems;
    }

    private static CallToolResult Plan(JsonElement input, PageDrawingGeometry page, string? displayLanguage, Func<JsonElement, double[]?>? measureText, string? layoutWarnings = null)
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
            JsonNode actual = Snap(item, page.GridSize, snap, normalizeRectangle: false);
            string field = Field(kind);
            JsonArray[] points = IsText(kind) || kind is "arc" or "ellipse" ? [actual[field]!.AsArray()] : [.. actual[field]!.AsArray().Select(p => p!.AsArray())];
            if (points.SelectMany(p => p).Any(n => !double.IsFinite(n!.GetValue<double>()) || n.GetValue<double>() is < 0 or > 10000))
            { problems.Add(at + ": snapping must produce finite coordinates in 0..10000 mm."); continue; }
            if (IsText(kind) && !item.GetProperty("contents").TryGetProperty("??_??", out _))
            {
                if (displayLanguage is null)
                { return Missing("placement.display_language", "Read the project's display language before planning language-specific text (A54)."); }
                if (!item.GetProperty("contents").TryGetProperty(displayLanguage, out _))
                { problems.Add(at + "/contents: include ??_?? or the project display language " + displayLanguage + " (A54)."); continue; }
            }
            double radius = kind == "arc" ? item.GetProperty("radius_mm").GetDouble() : kind == "ellipse" ? Math.Max(item.GetProperty("radius_x_mm").GetDouble(), item.GetProperty("radius_y_mm").GetDouble()) : 0;
            double[]? bounds = IsText(kind) ? measureText?.Invoke(JsonSerializer.SerializeToElement(actual)) : kind is "arc" or "ellipse" ?
                [points[0][0]!.GetValue<double>() - radius, points[0][1]!.GetValue<double>() - radius, points[0][0]!.GetValue<double>() + radius, points[0][1]!.GetValue<double>() + radius] :
                [points.Min(p => p[0]!.GetValue<double>()), points.Min(p => p[1]!.GetValue<double>()), points.Max(p => p[0]!.GetValue<double>()), points.Max(p => p[1]!.GetValue<double>())];
            if (bounds is not null && (bounds.Length != 4 || bounds.Any(n => !double.IsFinite(n)) || bounds[0] > bounds[2] || bounds[1] > bounds[3]))
            { bounds = null; }
            if (points.Any(p => p[0]!.GetValue<double>() > size[0] || p[1]!.GetValue<double>() > size[1]) || bounds is not null && (bounds[0] < 0 || bounds[1] < 0 || bounds[2] > size[0] || bounds[3] > size[1] || bounds.Any(n => n > 10000)))
            { problems.Add(at + ": the full outline after snapping must be inside Page.Size and 0..10000 mm."); }
            rows.Add(new JsonObject { ["requested"] = JsonNode.Parse(item.GetRawText()), ["actual"] = actual, ["bounds"] = JsonSerializer.SerializeToNode(bounds), ["snapped"] = !JsonElement.DeepEquals(item, JsonSerializer.SerializeToElement(actual)) });
            changes.Add($"create {kind} {ResultShaper.CleanFullText(page.Page)} item {i.ToString(CultureInfo.InvariantCulture)}: {actual.ToJsonString()} bounds {JsonSerializer.Serialize(bounds)}");
        }
        if (problems.Count > 0)
        { return Invalid(problems); }
        var result = new JsonObject { ["project"] = ResultShaper.CleanText(page.Project), ["page"] = ResultShaper.CleanText(page.Page), ["execution"] = ExecutionState.NotStarted, ["dryRun"] = true, ["gridSize"] = page.GridSize, ["boundarySource"] = "Page.Size", ["items"] = rows, ["changes"] = JsonSerializer.SerializeToNode(changes), ["connectionChanges"] = new JsonArray() };
        result["fingerprint"] = page.Fingerprint();
        result["hint"] = WriteToolDefaults.ExecutionHint;
        if (!string.IsNullOrEmpty(layoutWarnings))
        { result["warning"] = layoutWarnings; }
        int budget = input.TryGetProperty("response_format", out JsonElement format) && format.GetString() == "detailed" ? 20000 : 5000;
        return ResultShaper.Fits(result, budget) ? ToolResults.Success("Dry run: would create text/graphics; no connection changes. Nothing was changed.", result)
            : Invalid(["/response_format: use detailed or fewer items; the complete impact list cannot be truncated."]);
    }

    internal static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".emf", ".wmf"];
    internal static bool IsText(string? kind) => kind is "text" or "path_text";
    private static string Field(string kind) => IsText(kind) ? "position" : kind is "arc" or "ellipse" ? "center" : kind is "line" or "polyline" ? "points" : "corners";
    internal static JsonNode Snap(JsonElement item, double grid, bool snap, bool normalizeRectangle = true)
    {
        if (!double.IsFinite(grid) || grid <= 0)
        { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.CapabilityMissing, Message = "A reliable page grid is required." }); }
        JsonNode actual = JsonNode.Parse(item.GetRawText())!;
        string kind = item.GetProperty("kind").GetString()!, field = Field(kind);
        JsonArray[] points = IsText(kind) || kind is "arc" or "ellipse" ? [actual[field]!.AsArray()] : [.. actual[field]!.AsArray().Select(p => p!.AsArray())];
        foreach (JsonArray point in points)
        { for (int i = 0; i < 2; i++) { double n = point[i]!.GetValue<double>(); double value = snap ? Math.Round(n / grid, MidpointRounding.AwayFromZero) * grid : n; if (!double.IsFinite(value) || value is < 0 or > 10000) { throw new BridgeCallException(ToolResults.InvalidArgumentError(["/items: snapped coordinates must be finite and in 0..10000 mm."])); } point[i] = value; } }
        if (kind == "image" || kind == "rectangle" && normalizeRectangle)
        { actual[field] = JsonSerializer.SerializeToNode(new[] { new[] { points.Min(p => p[0]!.GetValue<double>()), points.Min(p => p[1]!.GetValue<double>()) }, new[] { points.Max(p => p[0]!.GetValue<double>()), points.Max(p => p[1]!.GetValue<double>()) } }); }
        return actual;
    }
    internal static PlacementText Request(JsonElement item)
    {
        string kind = item.GetProperty("kind").GetString()!, field = Field(kind);
        double[][] points = IsText(kind) || kind is "arc" or "ellipse" ? [item.GetProperty(field).Deserialize<double[]>()!] : item.GetProperty(field).Deserialize<double[][]>()!;
        return new()
        {
            Kind = kind,
            Position = points[0],
            EndPosition = kind is "line" or "rectangle" or "image" ? points[1] : [],
            Points = kind == "polyline" ? points : [],
            HeightMm = IsText(kind) ? item.TryGetProperty("height_mm", out JsonElement height) ? height.GetDouble() : -16002 : 0,
            Contents = IsText(kind) ? item.GetProperty("contents").Deserialize<Dictionary<string, string>>()! : [],
            RadiusMm = kind == "arc" ? item.GetProperty("radius_mm").GetDouble() : 0,
            RadiusXmm = kind == "ellipse" ? item.GetProperty("radius_x_mm").GetDouble() : 0,
            RadiusYmm = kind == "ellipse" ? item.GetProperty("radius_y_mm").GetDouble() : 0,
            Angle = item.TryGetProperty("angle", out JsonElement angle) ? angle.GetDouble() : 0,
            Layer = item.TryGetProperty("layer", out JsonElement layer) ? layer.GetInt32() : null,
            Pen = item.TryGetProperty("pen", out JsonElement pen) ? pen.Deserialize<Dictionary<string, double>>() : null,
            Alignment = item.TryGetProperty("alignment", out JsonElement alignment) ? alignment.GetString() : null,
            Closed = item.TryGetProperty("closed", out JsonElement closed) && closed.GetBoolean(),
            StartAngle = kind == "arc" ? item.GetProperty("start_angle").GetDouble() : 0,
            EndAngle = kind == "arc" ? item.GetProperty("end_angle").GetDouble() : 0,
            File = kind == "image" ? ImportFiles.Normalized(item.GetProperty("file").GetString()!) : ""
        };
    }
    internal static async Task<PlacementText> RequestAsync(JsonElement item, ImportFiles? files, CancellationToken cancellationToken)
    {
        PlacementText request = Request(item);
        if (request.Kind == "image")
        {
            if (files is null)
            { throw new ImportFileException("The imports reader is unavailable."); }
            ImportFile source = await files.ResolveAsync("file", request.File, ImportTools.MaxDrawingBytes, cancellationToken).ConfigureAwait(false);
            request.File = source.Path;
            request.FilePath = source.FullPath;
            request.Sha256 = source.Sha256;
        }
        return request;
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
