using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A54/A57: read-only absolute planning; property children share their parent's identifier, never its digest.</summary>
public static class PlacementMovePlanner
{
    public static JObject Plan(string json, ConnectionCreatePlanner.Scene scene, string? expectedFingerprint = null)
    {
        try
        { return Predict(json, scene, expectedFingerprint); }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
    }

    private static JObject Predict(string json, ConnectionCreatePlanner.Scene scene, string? expectedFingerprint)
    {
        Require(System.Text.Encoding.UTF8.GetByteCount(json) <= 256 * 1024, "", "Keep requests within 256 KiB.");
        JObject input;
        try
        {
            using var parser = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            input = JObject.Load(parser, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Require(!parser.Read(), "", "Supply one JSON object.");
        }
        catch (JsonReaderException) { throw WriteArguments.Invalid(["/: Supply strict JSON without duplicate members."]); }
        if (input.ContainsKey("property_moves"))
        { return PropertyPlacementMoves.Plan(input, scene, expectedFingerprint); }
        ConnectionCreatePlanner.Members(input, "", "project page moves", "dry_run snap_to_grid response_format layout_guard");
        string guard = LayoutGuard.Mode(input["layout_guard"]);
        ConnectionCreatePlanner.FiniteNumbers(input, "");
        Require(input["project"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["project"]), "/project", "Specify an open project.");
        Require(input["page"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["page"]), "/page", "Specify an exact Circuit page.");
        foreach (string name in new[] { "dry_run", "snap_to_grid" })
        { Require(input[name] is null || input[name]!.Type == JTokenType.Boolean, "/" + name, "Supply a boolean."); }
        Require(input["response_format"] is null || input["response_format"]!.Type == JTokenType.String && (string?)input["response_format"] is "concise" or "detailed", "/response_format", "Use concise or detailed.");
        Require(input["moves"] is JArray { Count: >= 1 and <= 50 }, "/moves", "Supply 1..50 moves.");
        string fingerprint = Fingerprint.Of([input.ToString(Formatting.None), BridgeJson.FromObject(scene).ToString(Formatting.None)]);
        if (expectedFingerprint is not null && expectedFingerprint != fingerprint)
        { throw Stale("Page layout, children or connections changed; create a new dry-run plan."); }
        var targets = new List<(DrawingPlacement Row, double[] Requested)>();
        foreach ((JToken move, int i) in input["moves"]!.Select((m, i) => (m, i)))
        {
            string at = "/moves/" + i.ToString(CultureInfo.InvariantCulture);
            bool delta = move is JObject obj && obj.ContainsKey("delta_mm");
            ConnectionCreatePlanner.Members(move, at, delta ? "locator delta_mm" : "locator x_mm y_mm", "");
            ConnectionCreatePlanner.Members(move["locator"], at + "/locator", "project page type oid digest", "");
            JToken locator = move["locator"]!;
            Require(locator["project"]?.Type == JTokenType.String && locator["page"]?.Type == JTokenType.String && locator["type"]?.Type == JTokenType.Integer && locator["oid"]?.Type == JTokenType.Integer
                && int.TryParse(locator["type"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int type) && type > 0
                && long.TryParse(locator["oid"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long oid) && oid > 0 && locator["digest"]?.Type == JTokenType.String
                && ((string)locator["digest"]!).Length == 64 && ((string)locator["digest"]!).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), at + "/locator", "Use the observed drawing locator with lowercase SHA-256.");
            DrawingLocator given = locator.ToObject<DrawingLocator>(BridgeJson.Serializer)!;
            DrawingPlacement row = scene.Page.Placements.SingleOrDefault(p => DrawingLocators.Matches(given, p.Locator)) ?? throw Stale("A locator changed; read eplan_page_get with include_drawings again.");
            Require(row.Parent is null && row.Kind != "PropertyPlacement", at + "/locator", "Move property text only through its parent; do not specify parent and child together.");
            Require(!targets.Any(t => Same(t.Row.Locator, row.Locator)), at + "/locator", "Remove duplicate targets.");
            JArray coordinates = delta ? move["delta_mm"] as JArray ?? new JArray() : new JArray(move["x_mm"]!, move["y_mm"]!);
            Require(coordinates.Count == 2 && coordinates.All(n => n.Type is JTokenType.Integer or JTokenType.Float && (double)n >= (delta ? -10000 : 0) && (double)n <= 10000), at, "Supply two finite mm coordinates; deltas may be negative.");
            if (row.Location.Length != 2 || row.Location.Any(n => double.IsNaN(n) || double.IsInfinity(n)))
            { throw Missing("Read the full placement location."); }
            targets.Add((row, coordinates.Select((n, j) => (double)n + (delta ? row.Location[j] : 0)).ToArray()));
        }
        PageDrawingGeometry page = scene.Page;
        if (!scene.Complete || scene.PageType != "Circuit" || page.Project != scene.Project.Path || (string?)input["page"] != page.Page
            || ((string?)input["project"] != scene.Project.Name && !ProjectResolver.SamePath((string)input["project"]!, page.Project))
            || !DrawingCoordinates.Contains(page.PageSize, [[0, 0, 0, 0]]) || page.Placements.Count > 10000 || scene.Pins.Count > 10000)
        { throw Missing("Read a complete Circuit page with reliable Page.Size and full placement outlines."); }
        var offsets = new Dictionary<DrawingPlacement, double[]>();
        var rows = new JArray();
        DrawingPlacement[] cables = page.Placements.Where(p => p.Kind == "Cable" && targets.Any(t => Same(p.Locator, t.Row.Locator)
            || p.Parent is not null && Same(p.Parent, t.Row.Locator))).ToArray();
        DrawingCoordinates.Snap(0, page.GridSize);
        foreach (var target in targets)
        {
            double[] actual = target.Requested.SequenceEqual(target.Row.Location) ? target.Requested : target.Requested.Select(n => DrawingCoordinates.Snap(n, page.GridSize, (bool?)input["snap_to_grid"] ?? true)).ToArray();
            double[] offset = actual.Zip(target.Row.Location, (a, b) => a - b).ToArray();
            // A75: EPLAN reconciles this cable's definition points only when connections are generated.
            DrawingPlacement[] closure = [target.Row, .. page.Placements.Where(p => p.Parent is not null && Same(p.Parent, target.Row.Locator))];
            foreach (DrawingPlacement child in closure.Where(p => !cables.Any(c => IsCablePoint(p, c))))
            {
                if (!child.Properties.TryGetValue("grouped", out string? grouped) || grouped != "false" || child.Kind is not ("Function" or "SymbolReference" or "PotentialDefinition" or "InterruptionPoint" or "Cable" or "ConnectionDefinitionPoint" or "Text" or "Line" or "Rectangle" or "PropertyPlacement"))
                { throw Missing("Grouped objects, macro groups and unverified placement kinds cannot be moved."); }
                offsets.Add(child, offset);
                // A54: all-zero outlines have no visible ink; unknown positions do not constrain the move.
                DrawingPlacement shifted = LayoutGuard.Shift(child, offset);
                double[] bounds = shifted.Bounds ?? (shifted.Location.Length == 2 ? [shifted.Location[0], shifted.Location[1], shifted.Location[0], shifted.Location[1]] : []);
                Require(!LayoutGuard.Positioned(child) || DrawingCoordinates.Contains(page.PageSize, [bounds]) && bounds.All(n => n <= 10000), "/moves", string.Format(System.Globalization.CultureInfo.InvariantCulture, "Keep every following outline inside Page.Size and 0..10000 mm ({0} at {1}: outline {2} moved to {3}).",
                    child.Kind, string.Join(",", child.Location), string.Join(",", child.Bounds ?? []), string.Join(",", bounds)));
                rows.Add(new JObject { ["locator"] = BridgeJson.FromObject(child.Locator), ["parent"] = child.Parent is null ? null : BridgeJson.FromObject(child.Parent), ["before"] = new JArray(child.Location), ["requested"] = new JArray(child.Location.Select((n, j) => n + target.Requested[j] - target.Row.Location[j])), ["after"] = new JArray(shifted.Location), ["snapped"] = !actual.SequenceEqual(target.Requested), ["bounds"] = shifted.Bounds is null ? null : new JArray(shifted.Bounds) });
            }
        }
        bool noOp = offsets.Values.All(d => d.All(n => n == 0));
        string Owner(string pin) => string.Join("/", pin.Split('/').Take(2));
        string Id(DrawingLocator locator) => FormattableString.Invariant($"{locator.Type}/{locator.Oid}");
        bool movesPins = offsets.Any(move => move.Value.Any(n => n != 0)
            && scene.Pins.Keys.Any(pin => Owner(pin) == Id(move.Key.Locator)));
        LayoutGuard.Model layout = LayoutGuard.From(scene);
        layout.SetsTextAttribute = LayoutGuard.TextMoveIntent(targets.Select(t => t.Row).ToArray());
        if (movesPins)
        {
            layout.Lines.Clear();
        }
        DrawingPlacement[] proposed = offsets.Where(p => p.Value.Any(n => n != 0)).Select(p => LayoutGuard.Shift(p.Key, p.Value)).ToArray();
        List<string> layoutWarnings = LayoutGuard.Enforce(layout, proposed, "/moves: ", mode: guard);
        double[] Box(DrawingPlacement p)
        {
            double[] d = offsets.TryGetValue(p, out double[]? o) ? o : [0, 0];
            return p.Bounds?.All(n => n == 0) != false
                ? [p.Location[0] + d[0], p.Location[1] + d[1], p.Location[0] + d[0], p.Location[1] + d[1]]
                : p.Bounds!.Select((n, j) => n + d[j % 2]).ToArray();
        }
        DrawingPlacement Predicted(DrawingPlacement p) => offsets.TryGetValue(p, out double[]? d) ? LayoutGuard.Shift(p, d) : p;
        DrawingPlacement[] predicted = page.Placements.Select(Predicted).ToArray();
        foreach (DrawingPlacement moved in offsets.Keys.Where(p => LayoutGuard.Positioned(p) && offsets[p].Any(n => n != 0)))
        {
            double[] a = Box(moved);
            Require(guard == "warn" || !page.Placements.Any(p => LayoutGuard.Positioned(p) && p != moved && !LayoutGuard.Related(Predicted(p), Predicted(moved), predicted) && Box(p) is var b
                && a[0] <= b[2] && a[2] >= b[0] && a[1] <= b[3] && a[3] >= b[1]), "/moves", "A moved outline conflicts with another placement.");
        }
        var pins = scene.Pins.ToDictionary(p => p.Key, p => new SymbolConnectionPoint { Direction = p.Value.Direction, Location = p.Value.Location.ToArray() }, StringComparer.Ordinal);
        var before = new Dictionary<string, double[][]>(StringComparer.Ordinal);
        var after = new Dictionary<string, double[][]>(StringComparer.Ordinal);
        string[] changed = [];
        if (movesPins)
        {
            if (pins.Values.Any(p => p.Location.Length != 2 || p.Location.Any(n => double.IsNaN(n) || double.IsInfinity(n)) || Opposite(p.Direction) == "")
                || scene.Logical.Any(c => new[] { c.StartDevice, c.EndDevice }.Any(k => k is not null && !pins.ContainsKey(k)))
                || pins.Any(p => !page.Placements.Any(r => r.Parent is null && Id(r.Locator) == Owner(p.Key))))
            {
                throw Missing("Read finite cardinal pin geometry with known owners.");
            }
            before = Autoconnect(pins);
            string Segment(double[][] s) => string.Join("|", s.Select(p => string.Join(",", p.Select(n => n.ToString("R", CultureInfo.InvariantCulture)))).OrderBy(p => p, StringComparer.Ordinal));
            if (!before.Values.Select(Segment).OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(scene.Graphical.Select(Segment).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)))
            {
                throw Missing("Autoconnect geometry is not fully predictable from this page's pins.");
            }
            foreach (var pin in pins)
            {
                double[]? offset = targets.Where(t => Id(t.Row.Locator) == Owner(pin.Key)).Select(t => offsets[t.Row]).SingleOrDefault();
                if (offset is not null)
                {
                    pin.Value.Location = pin.Value.Location.Zip(offset, (a, b) => a + b).ToArray();
                }
            }
            after = Autoconnect(pins);
            layout.Pins = pins;
            layout.Lines = after.Select((p, i) => new LayoutGuard.Segment { Points = p.Value, Changed = !before.TryGetValue(p.Key, out double[][]? old) || Segment(old) != Segment(p.Value), Net = p.Key, Locator = new() { Oid = -i - 1000 } }).ToList();
            layoutWarnings.AddRange(LayoutGuard.Enforce(layout, proposed, "/moves: ", mode: guard));
            Require(!pins.GroupBy(p => string.Join(",", p.Value.Location.Select(n => n.ToString("R", CultureInfo.InvariantCulture))), StringComparer.Ordinal).Any(g => g.Select(p => Owner(p.Key)).Distinct(StringComparer.Ordinal).Count() > 1), "/moves", "Pins from different placements must not coincide.");
            // A64/A75: these annotate wires; EPLAN resolves cable membership after generation, then eplan_page_review checks it.
            foreach (DrawingPlacement moved in offsets.Keys.Where(p => p.Kind is not ("Cable" or "ConnectionDefinitionPoint") && LayoutGuard.Positioned(p) && offsets[p].Any(n => n != 0)))
            {
                double[] b = Box(moved);
                Require(guard == "warn" || !after.Any(l => !l.Key.Split('|').Any(k => Owner(k) == Id(moved.Parent ?? moved.Locator)) && Math.Min(l.Value[0][0], l.Value[1][0]) <= b[2] && Math.Max(l.Value[0][0], l.Value[1][0]) >= b[0] && Math.Min(l.Value[0][1], l.Value[1][1]) <= b[3] && Math.Max(l.Value[0][1], l.Value[1][1]) >= b[1]), "/moves", "A moved outline conflicts with an autoconnect line.");
            }
            changed = before.Keys.Union(after.Keys, StringComparer.Ordinal).Where(k => !before.ContainsKey(k) || !after.ContainsKey(k) || Segment(before[k]) != Segment(after[k])).ToArray();
            foreach (string key in changed.Where(after.ContainsKey))
            {
                double[][] line = after[key];
                Require(!after.Any(l => l.Key != key && ConnectionCreatePlanner.Intersects(line, l.Value) && !l.Value.Any(a => line.Any(b => a.SequenceEqual(b)))), "/moves", "Changed autoconnect lines have an ambiguous crossing.");
                Require(guard == "warn" || !page.Placements.Any(p => LayoutGuard.Positioned(p) && !key.Split('|').Any(k => Owner(k) == Id(p.Parent ?? p.Locator)) && Box(p) is var b
                    && Math.Min(line[0][0], line[1][0]) <= b[2] && Math.Max(line[0][0], line[1][0]) >= b[0] && Math.Min(line[0][1], line[1][1]) <= b[3] && Math.Max(line[0][1], line[1][1]) >= b[1]), "/moves", "A changed autoconnect line conflicts with another placement.");
            }
        }
        else
        {
            // No moved placement owns a pin, so autoconnect stays as drawn; an outline moved onto one of its lines is still refused.
            foreach (DrawingPlacement moved in offsets.Keys.Where(p => p.Kind is not ("Cable" or "ConnectionDefinitionPoint") && LayoutGuard.Positioned(p) && offsets[p].Any(n => n != 0)))
            {
                double[] b = Box(moved);
                Require(guard == "warn" || !scene.Graphical.Any(l => l.Length >= 2 && Math.Min(l[0][0], l[1][0]) <= b[2] && Math.Max(l[0][0], l[1][0]) >= b[0]
                    && Math.Min(l[0][1], l[1][1]) <= b[3] && Math.Max(l[0][1], l[1][1]) >= b[1]), "/moves", "A moved outline conflicts with an autoconnect line.");
            }
        }
        Require(pins.Values.All(p => p.Location.All(n => n is >= 0 and <= 10000) && DrawingCoordinates.Contains(page.PageSize, [[p.Location[0], p.Location[1], p.Location[0], p.Location[1]]])), "/moves", "Keep all following pins inside Page.Size.");
        var affected = new HashSet<string>(changed.SelectMany(k => k.Split('|')), StringComparer.Ordinal);
        bool expanded;
        do
        {
            int count = affected.Count;
            foreach (var pin in pins.Where(p => affected.Any(a => Owner(a) == Owner(p.Key)) && page.Placements.Any(r => r.Parent is null && Id(r.Locator) == Owner(p.Key) && r.Kind != "Function")).ToArray())
            { affected.Add(pin.Key); }
            foreach (string edge in before.Keys.Concat(after.Keys).Where(k => k.Split('|').Any(affected.Contains)).ToArray())
            { affected.UnionWith(edge.Split('|')); }
            expanded = affected.Count != count;
        } while (expanded);
        var result = new JObject
        {
            ["project"] = scene.Project.Name,
            ["page"] = page.Page,
            ["execution"] = ExecutionState.NotStarted,
            ["dryRun"] = true,
            ["noOp"] = noOp,
            ["gridSize"] = page.GridSize,
            ["boundarySource"] = "Page.Size",
            ["moves"] = rows,
            ["fingerprint"] = fingerprint,
            ["graphicalChanges"] = new JArray(changed.Select(k => new JObject { ["pins"] = new JArray(k.Split('|')), ["before"] = before.TryGetValue(k, out double[][]? b) ? JArray.FromObject(b) : null, ["after"] = after.TryGetValue(k, out double[][]? a) ? JArray.FromObject(a) : null })),
            ["connectionChanges"] = JArray.FromObject(noOp ? [] : scene.Logical.Where(c => affected.Contains(c.StartDevice ?? "") || affected.Contains(c.EndDevice ?? "")).Select(c => new[] { c.StartDevice, c.EndDevice })),
            ["generation"] = "Logical endpoints are preserved; use eplan_connection_generate after moving to regenerate connections."
        };
        if (offsets.Any(p => p.Key.Kind == "Cable" && p.Value.Any(n => n != 0)))
        { layoutWarnings.Add("Moving a cable drops its core assignment (A73): EPLAN recreates its definition points unnamed and eplan_connection_generate does not make the crossed connections cable cores again. Check the cores with eplan_page_review; undo (Ctrl+Z) restores them."); }
        result["warning"] = layoutWarnings.Count == 0 ? null : LayoutGuard.Summary(layoutWarnings);
        string encoded = result.ToString(Formatting.None);
        Require((encoded.Count(c => c <= 127) + 3) / 4 + encoded.Count(c => c > 127) <= ((string?)input["response_format"] == "detailed" ? 20000 : 5000) - 256, "/response_format", "Use detailed or fewer moves; complete impact cannot be truncated.");
        return result;
    }

    internal static bool IsCablePoint(DrawingPlacement point, DrawingPlacement cable) => point.Kind == "ConnectionDefinitionPoint"
        && cable.Properties.TryGetValue("name", out string? name) && !string.IsNullOrWhiteSpace(name)
        && point.Properties.TryGetValue("name", out string? owner) && string.Equals(name, owner, StringComparison.Ordinal);

    /// <summary>
    /// Live 2026-11-02 (A75): at a moved cable's new crossing EPLAN adds a point with a new identity whose name is only a
    /// structure prefix ("+") until generation assigns it; any other name or position stays a mismatch.
    /// </summary>
    internal static bool IsNewCablePoint(DrawingPlacement point, IReadOnlyList<DrawingPlacement> before, IEnumerable<DrawingPlacement> cables)
    {
        const double tolerance = 0.5;
        string name = point.Properties.TryGetValue("name", out string? value) ? value ?? "" : "";
        return point.Kind == "ConnectionDefinitionPoint" && point.Location.Length == 2 && name.All(c => "=+&#-".Contains(c))
            && !before.Any(p => p.Locator.Type == point.Locator.Type && p.Locator.Oid == point.Locator.Oid)
            && cables.Any(c => c.Bounds is { Length: 4 } b && point.Location[0] >= b[0] - tolerance && point.Location[0] <= b[2] + tolerance
                && point.Location[1] >= b[1] - tolerance && point.Location[1] <= b[3] + tolerance);
    }

    public static Dictionary<string, double[][]> Autoconnect(Dictionary<string, SymbolConnectionPoint> pins)
    {
        string Owner(string pin) => string.Join("/", pin.Split('/').Take(2));
        return pins.SelectMany(a => pins.Where(b => a.Key != b.Key && Owner(a.Key) != Owner(b.Key)
            && Facing(a.Value, b.Value)).OrderBy(b => Math.Abs(b.Value.Location[0] - a.Value.Location[0]) + Math.Abs(b.Value.Location[1] - a.Value.Location[1])).Take(1).Where(b => b.Value.Direction == Opposite(a.Value.Direction))
            .Select(b => (Key: string.Join("|", new[] { a.Key, b.Key }.OrderBy(k => k, StringComparer.Ordinal)), Points: new[] { a.Value.Location, b.Value.Location })))
            .GroupBy(l => l.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Points, StringComparer.Ordinal);
    }
    private static bool Same(DrawingLocator a, DrawingLocator b) => ProjectResolver.SamePath(a.Project, b.Project) && a.Page == b.Page && a.Type == b.Type && a.Oid == b.Oid;
    private static string Opposite(string d) => d switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", "Right" => "Left", _ => "" };
    private static bool Facing(SymbolConnectionPoint a, SymbolConnectionPoint b) => a.Direction switch { "Up" => a.Location[0] == b.Location[0] && a.Location[1] < b.Location[1], "Down" => a.Location[0] == b.Location[0] && a.Location[1] > b.Location[1], "Left" => a.Location[1] == b.Location[1] && a.Location[0] > b.Location[0], _ => a.Location[1] == b.Location[1] && a.Location[0] < b.Location[0] };
    private static void Require(bool valid, string at, string message) { if (!valid) { throw WriteArguments.Invalid([at + ": " + message]); } }
    private static BridgeErrorException Missing(string message) => new(ErrorReason.CapabilityMissing, message, metadata: new Dictionary<string, string> { ["missing"] = "placement.update.execute" });
    private static BridgeErrorException Stale(string message) => new(ErrorReason.PlanStale, message);
}
