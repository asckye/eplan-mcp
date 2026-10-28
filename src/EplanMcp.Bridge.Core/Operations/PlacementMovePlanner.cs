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
        ConnectionCreatePlanner.Members(input, "", "project page moves", "dry_run snap_to_grid response_format");
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
            || !DrawingCoordinates.Contains(page.PageSize, page.Placements.Select(p => p.Bounds)) || page.Placements.Count > 10000 || scene.Pins.Count > 10000)
        { throw Missing("Read a complete Circuit page with reliable Page.Size and full placement outlines."); }
        var offsets = new Dictionary<DrawingPlacement, double[]>();
        var rows = new JArray();
        DrawingCoordinates.Snap(0, page.GridSize);
        foreach (var target in targets)
        {
            double[] actual = target.Requested.SequenceEqual(target.Row.Location) ? target.Requested : target.Requested.Select(n => DrawingCoordinates.Snap(n, page.GridSize, (bool?)input["snap_to_grid"] ?? true)).ToArray();
            double[] offset = actual.Zip(target.Row.Location, (a, b) => a - b).ToArray();
            DrawingPlacement[] closure = [target.Row, .. page.Placements.Where(p => p.Parent is not null && Same(p.Parent, target.Row.Locator))];
            foreach (DrawingPlacement child in closure)
            {
                if (!child.Properties.TryGetValue("grouped", out string? grouped) || grouped != "false" || child.Kind is not ("Function" or "SymbolReference" or "PotentialDefinition" or "Text" or "Line" or "Rectangle" or "PropertyPlacement"))
                { throw Missing("Grouped objects, macro groups and unverified placement kinds cannot be moved."); }
                offsets.Add(child, offset);
                double[] bounds = child.Bounds!.Select((n, j) => n + offset[j % 2]).ToArray();
                Require(DrawingCoordinates.Contains(page.PageSize, [bounds]) && bounds.All(n => n <= 10000), "/moves", "Keep every following outline inside Page.Size and 0..10000 mm.");
                rows.Add(new JObject { ["locator"] = BridgeJson.FromObject(child.Locator), ["parent"] = child.Parent is null ? null : BridgeJson.FromObject(child.Parent), ["before"] = new JArray(child.Location), ["requested"] = new JArray(child.Location.Select((n, j) => n + target.Requested[j] - target.Row.Location[j])), ["after"] = new JArray(child.Location.Zip(offset, (a, b) => a + b)), ["snapped"] = !actual.SequenceEqual(target.Requested), ["bounds"] = new JArray(bounds) });
            }
        }
        bool noOp = offsets.Values.All(d => d.All(n => n == 0));
        double[] Box(DrawingPlacement p) => p.Bounds!.Select((n, j) => n + (offsets.TryGetValue(p, out double[]? d) ? d[j % 2] : 0)).ToArray();
        foreach (DrawingPlacement moved in offsets.Keys.Where(p => offsets[p].Any(n => n != 0)))
        {
            double[] a = Box(moved);
            Require(!page.Placements.Any(p => p != moved && !Same(p.Parent ?? p.Locator, moved.Parent ?? moved.Locator) && Box(p) is var b
                && a[0] <= b[2] && a[2] >= b[0] && a[1] <= b[3] && a[3] >= b[1]), "/moves", "A moved outline conflicts with another placement.");
        }
        string Owner(string pin) => string.Join("/", pin.Split('/').Take(2));
        string Id(DrawingLocator l) => FormattableString.Invariant($"{l.Type}/{l.Oid}");
        var pins = scene.Pins.ToDictionary(p => p.Key, p => new SymbolConnectionPoint { Direction = p.Value.Direction, Location = p.Value.Location.ToArray() }, StringComparer.Ordinal);
        if (pins.Values.Any(p => p.Location.Length != 2 || p.Location.Any(n => double.IsNaN(n) || double.IsInfinity(n)) || Opposite(p.Direction) == "")
            || scene.Logical.Any(c => new[] { c.StartDevice, c.EndDevice }.Any(k => k is not null && !pins.ContainsKey(k)))
            || pins.Any(p => !page.Placements.Any(r => r.Parent is null && Id(r.Locator) == Owner(p.Key))))
        { throw Missing("Read finite cardinal pin geometry with known owners."); }
        Dictionary<string, double[][]> Lines() => pins.SelectMany(a => pins.Where(b => a.Key != b.Key && Owner(a.Key) != Owner(b.Key)
            && Facing(a.Value, b.Value)).OrderBy(b => Math.Abs(b.Value.Location[0] - a.Value.Location[0]) + Math.Abs(b.Value.Location[1] - a.Value.Location[1])).Take(1).Where(b => b.Value.Direction == Opposite(a.Value.Direction))
            .Select(b => (Key: string.Join("|", new[] { a.Key, b.Key }.OrderBy(k => k, StringComparer.Ordinal)), Points: new[] { a.Value.Location, b.Value.Location })))
            .GroupBy(l => l.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Points, StringComparer.Ordinal);
        Dictionary<string, double[][]> before = Lines();
        string Segment(double[][] s) => string.Join("|", s.Select(p => string.Join(",", p.Select(n => n.ToString("R", CultureInfo.InvariantCulture)))).OrderBy(p => p, StringComparer.Ordinal));
        if (!before.Values.Select(Segment).OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(scene.Graphical.Select(Segment).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)))
        { throw Missing("Autoconnect geometry is not fully predictable from this page's pins."); }
        foreach (var pin in pins)
        {
            double[]? offset = targets.Where(t => Id(t.Row.Locator) == Owner(pin.Key)).Select(t => offsets[t.Row]).SingleOrDefault();
            if (offset is not null)
            { pin.Value.Location = pin.Value.Location.Zip(offset, (a, b) => a + b).ToArray(); }
        }
        Dictionary<string, double[][]> after = Lines();
        Require(!pins.GroupBy(p => string.Join(",", p.Value.Location.Select(n => n.ToString("R", CultureInfo.InvariantCulture))), StringComparer.Ordinal).Any(g => g.Select(p => Owner(p.Key)).Distinct(StringComparer.Ordinal).Count() > 1), "/moves", "Pins from different placements must not coincide.");
        foreach (DrawingPlacement moved in offsets.Keys.Where(p => offsets[p].Any(n => n != 0)))
        {
            double[] b = Box(moved);
            Require(!after.Any(l => !l.Key.Split('|').Any(k => Owner(k) == Id(moved.Parent ?? moved.Locator)) && Math.Min(l.Value[0][0], l.Value[1][0]) <= b[2] && Math.Max(l.Value[0][0], l.Value[1][0]) >= b[0] && Math.Min(l.Value[0][1], l.Value[1][1]) <= b[3] && Math.Max(l.Value[0][1], l.Value[1][1]) >= b[1]), "/moves", "A moved outline conflicts with an autoconnect line.");
        }
        string[] changed = before.Keys.Union(after.Keys, StringComparer.Ordinal).Where(k => !before.ContainsKey(k) || !after.ContainsKey(k) || Segment(before[k]) != Segment(after[k])).ToArray();
        Require(pins.Values.All(p => p.Location.All(n => n is >= 0 and <= 10000) && DrawingCoordinates.Contains(page.PageSize, [[p.Location[0], p.Location[1], p.Location[0], p.Location[1]]])), "/moves", "Keep all following pins inside Page.Size.");
        foreach (string key in changed.Where(after.ContainsKey))
        {
            double[][] line = after[key];
            Require(!after.Any(l => l.Key != key && ConnectionCreatePlanner.Intersects(line, l.Value) && !l.Value.Any(a => line.Any(b => a.SequenceEqual(b)))), "/moves", "Changed autoconnect lines have an ambiguous crossing.");
            Require(!page.Placements.Any(p => !key.Split('|').Any(k => Owner(k) == Id(p.Parent ?? p.Locator)) && Box(p) is var b
                && Math.Min(line[0][0], line[1][0]) <= b[2] && Math.Max(line[0][0], line[1][0]) >= b[0] && Math.Min(line[0][1], line[1][1]) <= b[3] && Math.Max(line[0][1], line[1][1]) >= b[1]), "/moves", "A changed autoconnect line conflicts with another placement.");
        }
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
        string encoded = result.ToString(Formatting.None);
        Require((encoded.Count(c => c <= 127) + 3) / 4 + encoded.Count(c => c > 127) <= ((string?)input["response_format"] == "detailed" ? 20000 : 5000) - 256, "/response_format", "Use detailed or fewer moves; complete impact cannot be truncated.");
        return result;
    }

    private static bool Same(DrawingLocator a, DrawingLocator b) => ProjectResolver.SamePath(a.Project, b.Project) && a.Page == b.Page && a.Type == b.Type && a.Oid == b.Oid;
    private static string Opposite(string d) => d switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", "Right" => "Left", _ => "" };
    private static bool Facing(SymbolConnectionPoint a, SymbolConnectionPoint b) => a.Direction switch { "Up" => a.Location[0] == b.Location[0] && a.Location[1] < b.Location[1], "Down" => a.Location[0] == b.Location[0] && a.Location[1] > b.Location[1], "Left" => a.Location[1] == b.Location[1] && a.Location[0] > b.Location[0], _ => a.Location[1] == b.Location[1] && a.Location[0] < b.Location[0] };
    private static void Require(bool valid, string at, string message) { if (!valid) { throw WriteArguments.Invalid([at + ": " + message]); } }
    private static BridgeErrorException Missing(string message) => new(ErrorReason.CapabilityMissing, message, metadata: new Dictionary<string, string> { ["missing"] = "placement.update.execute" });
    private static BridgeErrorException Stale(string message) => new(ErrorReason.PlanStale, message);
}
