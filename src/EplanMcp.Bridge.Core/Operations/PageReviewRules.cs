using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Detached, read-only page rules; electrical crossings require target-network evidence (A52/A64/A65).</summary>
public static class PageReviewRules
{
    private static readonly string[] _coreFields = ["label", "colour"];
    // One page review shares this search time, like the drawing tools (LayoutGuard.FreePositionSearchBudget).
    private static readonly TimeSpan _freeSearchBudget = TimeSpan.FromSeconds(2);
    // Review check name -> issue code, and layout rule -> review check.
    private static readonly Dictionary<string, string> _guardChecks = new(StringComparer.Ordinal)
    { ["wire_through_symbol"] = "wire_through_symbol", ["net_overlap"] = "net_overlap", ["text_format"] = "text_format", ["cross_reference_clash"] = "cross_reference_clash" };
    private static readonly Dictionary<string, string> _guardRules = new(StringComparer.Ordinal)
    { ["R4"] = "wire_through_symbol", ["R5"] = "net_overlap", ["R6"] = "net_overlap", ["R8"] = "text_format", ["R9"] = "cross_reference_clash" };

    /// <summary>
    /// The layout guard over every placement and wire of the page, as measured: nets come from shared wire ends, polylines
    /// are split into segments, and no free position is searched.
    /// </summary>
    private static double Deep(ConnectionCreatePlanner.Scene scene) => Math.Max(1, scene.Page.GridSize / 2);
    private static string Device(DrawingPlacement p) => p.Properties.TryGetValue("name", out string? name) ? name.Split(':')[0] : "";
    /// <summary>One device, or a device and its sub-device (live 2026-11-16: the pins of -KK1-X11 sit on the -KK1 box edge).</summary>
    private static bool SameDevice(DrawingPlacement p, DrawingPlacement q)
    {
        string a = Device(p), b = Device(q);
        return a.Length > 0 && b.Length > 0 && (a == b || a.StartsWith(b + "-", StringComparison.Ordinal) || b.StartsWith(a + "-", StringComparison.Ordinal));
    }
    /// <summary>
    /// Live 2026-11-16 (official =F+A2&amp;EFS1/1): PLC plug outlines of 44 x 34 mm cover the pins of their neighbours; a wire that
    /// ends on a pin inside the outline connects there and does not cross the symbol.
    /// </summary>
    private static bool EndsInside(ConnectionCreatePlanner.Scene scene, LayoutGuard.Violation v)
    {
        long line = v.Objects.Select(o => o.Oid).FirstOrDefault(oid => oid < 0);
        if (line == 0 || -line - 1 >= scene.Graphical.Count || v.Geometry.Length != 4)
        { return false; }
        double[] b = v.Geometry;
        double[][] route = scene.Graphical[(int)(-line - 1)];
        // Live 2026-11-16 (=EC1+A3&EFS1/1): a wire through the symbol's own connection point is connected there.
        string owner = FormattableString.Invariant($"{v.Objects[0].Type}/{v.Objects[0].Oid}/");
        if (scene.Pins.Where(pin => pin.Key.StartsWith(owner, StringComparison.Ordinal)).Any(pin => route.Zip(route.Skip(1), (a, c) => OnSegment(pin.Value.Location, a, c)).Any(on => on)))
        { return true; }
        return route.Any(end => end[0] >= b[0] - 1e-6 && end[0] <= b[2] + 1e-6 && end[1] >= b[1] - 1e-6 && end[1] <= b[3] + 1e-6
            && scene.Pins.Values.Any(pin => pin.Location.Length == 2 && Math.Abs(pin.Location[0] - end[0]) < 1e-6 && Math.Abs(pin.Location[1] - end[1]) < 1e-6));
    }
    private static bool OnSegment(double[] p, double[] a, double[] b) => p.Length == 2
        && Math.Abs((b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])) < 1e-6
        && p[0] >= Math.Min(a[0], b[0]) - 1e-6 && p[0] <= Math.Max(a[0], b[0]) + 1e-6 && p[1] >= Math.Min(a[1], b[1]) - 1e-6 && p[1] <= Math.Max(a[1], b[1]) + 1e-6;
    internal static List<LayoutGuard.Violation> GuardViolations(ConnectionCreatePlanner.Scene scene) =>
        LayoutGuard.Check(GuardModel(scene, linesChanged: true), [.. scene.Page.Placements.Where(LayoutGuard.Positioned)], suggest: false, measured: true, searchViolations: false);

    /// <summary>
    /// The nearest grid position where the symbol and its texts clear every rule, as the drawing tools search it; null when
    /// none is found or the shared search time is used up. Existing wires count as obstacles, not as changes.
    /// </summary>
    private static double[]? FreePosition(ConnectionCreatePlanner.Scene scene, ref LayoutGuard.Model? model, DrawingPlacement symbol, Func<bool> expired)
    {
        if (expired() || !LayoutGuard.Positioned(symbol))
        { return null; }
        model ??= GuardModel(scene, linesChanged: false);
        DrawingPlacement[] group = [symbol, .. scene.Page.Placements.Where(t => t.Parent is { } owner && owner.Oid == symbol.Locator.Oid && owner.Type == symbol.Locator.Type && LayoutGuard.Positioned(t))];
        // The position must also pass eplan_placement_update's own move rule, or the suggested fix is refused (live 2026-11-16).
        return LayoutGuard.FindFree(model, symbol, group, measured: true, expired: expired,
            accept: trial => LayoutGuard.ClearOfOthers([.. scene.Page.Placements.Where(p => !group.Contains(p)), .. trial], trial));
    }

    private static LayoutGuard.Model GuardModel(ConnectionCreatePlanner.Scene scene, bool linesChanged)
    {
        IEnumerable<LayoutGuard.Segment> segments = scene.Graphical.Select((s, i) => (s, i)).Where(x => x.s.Length >= 2).SelectMany(x => x.s.Zip(x.s.Skip(1), (a, b) => new LayoutGuard.Segment
        { Points = [a, b], Locator = new() { Oid = -x.i - 1 }, Changed = linesChanged })).Where(s => !s.Points[0].SequenceEqual(s.Points[1]));
        return new LayoutGuard.Model
        {
            AllowUnknownGeometry = true,
            Page = scene.Page,
            Config = scene.Layout,
            Pins = new(scene.Pins),
            PointRoutes = scene.PointRoutes,
            PointRouteGaps = scene.PointRouteGaps,
            Lines = LayoutGuard.Networks(segments),
        };
    }
    public static JArray Check(ConnectionCreatePlanner.Scene scene, JObject actual, JObject request)
    {
        var issues = new JArray();
        JObject targetsByKind = request["expected"] as JObject ?? new();
        bool Enabled(string check) => request["checks"] is not JArray checks || checks.Values<string>().Contains(check, StringComparer.Ordinal);
        void Add(string code, string severity, double[] position, IEnumerable<DrawingLocator> objects, string message, string tool, JObject args)
        {
            DrawingLocator[] locators = objects.ToArray();
            if (position.Length != 2)
            { position = scene.Page.Placements.FirstOrDefault(p => locators.Any(l => l.Oid == p.Locator.Oid))?.Location ?? actual["connections"]?.FirstOrDefault(c => locators.Any(l => l.Oid == (long?)c["locator"]?["oid"]))?["position"]?.ToObject<double[]>() ?? []; }
            if (tool is "eplan_placement_update" or "eplan_connection_create")
            { args["project"] = scene.Project.Name; args["page"] = scene.Page.Page; }
            if (tool == "eplan_placement_update" && args["locator"] is { } locator)
            { args = new JObject { ["project"] = scene.Project.Name, ["page"] = scene.Page.Page, ["moves"] = new JArray(args["x"] is { } x ? new JObject { ["locator"] = locator.DeepClone(), ["x_mm"] = x.DeepClone(), ["y_mm"] = args["y"]!.DeepClone() } : new JObject { ["locator"] = locator.DeepClone() }) }; }
            issues.Add(new JObject { ["code"] = code, ["severity"] = severity, ["page"] = scene.Page.Page, ["position"] = new JArray(position), ["objects"] = BridgeJson.FromObject(locators), ["message"] = message, ["fix"] = new JObject { ["tool"] = tool, ["args"] = args } });
        }
        DrawingPlacement? Owner(string pin) => scene.Page.Placements.FirstOrDefault(p => pin.StartsWith(FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}/"), StringComparison.Ordinal));
        static bool Equal(double[] a, double[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) < 1e-8).All(v => v);
        static bool Known(double[] point) => point.Length == 2 && point.All(v => !double.IsNaN(v) && !double.IsInfinity(v));
        // A72: an empty graphical representation is not an electrical edge.
        var graphical = scene.Graphical.Where(s => s.Length >= 2 && s.Any(p => !Equal(p, s[0]))).ToArray();
        bool Near(double[] a, double[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) <= ((double?)request["tolerance_mm"] ?? 0.5)).All(v => v);
        var placements = scene.Page.Placements.Where(p => p.Parent is null || p.Properties.ContainsKey("text")).ToArray();
        foreach (double[] point in graphical.SelectMany(s => s).Where(Known).GroupBy(p => string.Join("/", p)).Select(g => g.First()))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (Enabled("off_grid") && scene.Page.GridSize > 0 && !scene.Pins.Values.Any(p => Equal(p.Location, point)) && point.Any(v => Math.Abs(v / scene.Page.GridSize - Math.Round(v / scene.Page.GridSize)) > 1e-8))
            { Add("off_grid", "info", point, [], "Wire bend is off the page grid.", "eplan_connection_create", new() { ["page"] = scene.Page.Page }); }
        }
        // A73: bends are not ends, and the exact page origin is an unknown/default coordinate.
        foreach (double[] end in graphical.SelectMany(s => new[] { s[0], s[s.Length - 1] }).Where(Known).Where(p => p[0] != 0 || p[1] != 0).GroupBy(p => string.Join("/", p)).Select(g => g.First()))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (Enabled("dangling_ends") && !scene.Pins.Values.Any(p => Equal(p.Location, end)))
            { Add("dangling_end", "error", end, [], "Wire end has no symbol pin or node.", "eplan_connection_create", new() { ["page"] = scene.Page.Page }); }
        }
        bool Connected(double[] location) => graphical.Any(s => s.Any(p => Equal(p, location)));
        // A83: an interruption point is one wire end even with two connection points (BP); one connected pin covers it, an open one is
        // reported once, at its tail (the highest pin index, as the planner attaches wires).
        static int PinIndex(string key) => int.TryParse(key.Split('/').Last(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? index : -1;
        foreach (var pin in scene.Pins)
        {
            DrawingPlacement? owner = Owner(pin.Key);
            if (Enabled("unconnected_pins") && owner?.Properties.ContainsKey("name") == true && !Connected(pin.Value.Location))
            {
                string prefix = FormattableString.Invariant($"{owner.Locator.Type}/{owner.Locator.Oid}/");
                if (owner.Kind == "InterruptionPoint" && scene.Pins.Any(other => other.Key.StartsWith(prefix, StringComparison.Ordinal)
                    && (Connected(other.Value.Location) || PinIndex(other.Key) > PinIndex(pin.Key))))
                { continue; }
                string? designation = (string?)actual["devices"]?.FirstOrDefault(d => (long?)d["locator"]?["oid"] == owner.Locator.Oid)?["pins"]?[pin.Key.Split('/').Last()];
                bool required = targetsByKind?["connections"]?.Any(c => new[] { c["from"], c["to"] }.Any(e => e is JObject endpoint && (string?)endpoint["device"] is { } name && DrawingNames.SameName(name, owner.Properties["name"], scene.Page.Page) && (string?)endpoint["pin"] == designation)) == true;
                Add("unconnected_pin", required ? "error" : "warning", pin.Value.Location, [owner.Locator], "Symbol pin has no graphical connection.", "eplan_connection_create", new() { ["page"] = scene.Page.Page, ["pin_index"] = pin.Key.Split('/').Last() });
            }
        }
        LayoutGuard.Model? guardModel = null;
        var searchTimer = System.Diagnostics.Stopwatch.StartNew();
        bool Expired() => searchTimer.Elapsed >= _freeSearchBudget;
        foreach (DrawingPlacement p in placements)
        {
            if (Enabled("off_grid") && scene.Page.GridSize > 0 && p.Location.Any(v => Math.Abs(v / scene.Page.GridSize - Math.Round(v / scene.Page.GridSize)) > 1e-8))
            { Add("off_grid", "info", p.Location, [p.Locator], "Placement is off the page grid; library pin offsets are exempt.", "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(p.Locator), ["x"] = Math.Round(p.Location[0] / scene.Page.GridSize) * scene.Page.GridSize, ["y"] = Math.Round(p.Location[1] / scene.Page.GridSize) * scene.Page.GridSize }); }
            if (!Enabled("overlaps") || p.Bounds is not { Length: 4 } a || a[0] == a[2] || a[1] == a[3])
            { continue; }
            foreach (DrawingPlacement q in placements.SkipWhile(q => !ReferenceEquals(q, p)).Skip(1))
            {
                // Agent eval 2026-11-06: PLC boxes 24 mm apart touch; EPLAN read one edge as 235.9999999999999.
                if (q.Bounds is { Length: 4 } b && a[0] < b[2] - 1e-6 && a[2] > b[0] + 1e-6 && a[1] < b[3] - 1e-6 && a[3] > b[1] + 1e-6 && (p.Properties.ContainsKey("name") || p.Kind == "Text") && (q.Properties.ContainsKey("name") || q.Kind == "Text"))
                {
                    // Owner decision 2026-11-16: one symbol on another is a drawing error; overlapping texts stay a warning.
                    // Live 2026-11-16 (official =GAB1+A2&EFS1/1): a device symbol encloses its own connection points (-TB1, -TB1:13).
                    bool symbols = p.Kind != "Text" && q.Kind != "Text" && !LayoutGuard.Related(p, q, scene.Page.Placements) && !SameDevice(p, q)
                        // Live 2026-11-16: official pages touch by 0.25-1 mm (mated plugs, plug pins on a box edge, a breaker and its
                        // potential point); outlines that overlap by half a grid both ways sit on each other.
                        && Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]) >= Deep(scene) && Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]) >= Deep(scene);
                    // Owner 2026-11-16: an error carries the nearest free grid position, so a repair needs no trial moves.
                    double[]? free = symbols ? FreePosition(scene, ref guardModel, q, Expired) : null;
                    var move = new JObject { ["locator"] = BridgeJson.FromObject(q.Locator) };
                    if (free is not null)
                    { move["x"] = free[0]; move["y"] = free[1]; }
                    Add("overlap", symbols ? "error" : "warning", p.Location, [p.Locator, q.Locator], !symbols ? "Symbol or text bounds overlap."
                        : free is null ? "Symbol outlines overlap; no free grid position was found nearby, move it by hand." : FormattableString.Invariant($"Symbol outlines overlap; the nearest free position for the second symbol is [{free[0]}, {free[1]}] mm."), "eplan_placement_update", move);
                }
            }
        }
        // A145: the plot frame's evaluation grid keeps the title block, border and column header clear; without it the page edge.
        double[]? area = scene.Page.DrawingArea ?? (scene.Page.PageSize is { Length: 2 } size ? [0, 0, size[0], size[1]] : null);
        if (Enabled("off_area") && area is not null)
        {
            string where = scene.Page.DrawingArea is null ? "the page" : "the plot frame's drawing area";
            double Shift(double low, double high, double min, double max) => low < min - 1e-6 ? min - low : high > max + 1e-6 ? max - high : 0;
            double Snap(double v, double toward) => toward != 0 && scene.Page.GridSize > 0 ? (toward > 0 ? Math.Ceiling(v / scene.Page.GridSize - 1e-9) : Math.Floor(v / scene.Page.GridSize + 1e-9)) * scene.Page.GridSize : v;
            foreach (DrawingPlacement p in placements.Where(p => p.Bounds is { Length: 4 } b && b.All(v => !double.IsNaN(v) && !double.IsInfinity(v)) && !b.All(v => v == 0)))
            {
                double[] b = p.Bounds!;
                double dx = Shift(b[0], b[2], area[0], area[2]), dy = Shift(b[1], b[3], area[1], area[3]);
                if (dx == 0 && dy == 0)
                { continue; }
                // A text that belongs to a symbol moves with it; the fix moves the owner.
                DrawingLocator target = p.Parent is { } owner && p.Kind != "Text" ? owner : p.Locator;
                double[] location = scene.Page.Placements.FirstOrDefault(o => o.Locator.Oid == target.Oid && o.Locator.Type == target.Type && o.Parent is null)?.Location ?? p.Location;
                Add("off_area", "error", p.Location, [p.Locator], FormattableString.Invariant($"Outline [{b[0]}, {b[1]}, {b[2]}, {b[3]}] leaves {where} [{area[0]}, {area[1]}, {area[2]}, {area[3]}]; keep it clear of the border and title block."),
                    "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(target), ["x"] = Snap(location[0] + dx, dx), ["y"] = Snap(location[1] + dy, dy) });
            }
            foreach (double[] point in graphical.SelectMany(s => s).Where(Known).Where(p => p[0] != 0 || p[1] != 0).GroupBy(p => string.Join("/", p)).Select(g => g.First()))
            {
                if (point[0] < area[0] - 1e-6 || point[1] < area[1] - 1e-6 || point[0] > area[2] + 1e-6 || point[1] > area[3] + 1e-6)
                {
                    // Live 2026-11-16 (official =EC1+A3&EFS1/1): an invisible wire point at (8, -4) lies off the page, like the A73
                    // default coordinates; the drawing tools refuse off-page wires (ADR-0046 S2), so a point off the page only warns.
                    bool onPage = scene.Page.PageSize is not { Length: 2 } s || point[0] >= -1e-6 && point[1] >= -1e-6 && point[0] <= s[0] + 1e-6 && point[1] <= s[1] + 1e-6;
                    Add("off_area", onPage ? "error" : "warning", point, [], onPage
                        ? FormattableString.Invariant($"Wire point leaves {where}; reroute it inside [{area[0]}, {area[1]}, {area[2]}, {area[3]}].")
                        : "Wire point lies off the page and is not drawn; it may be an unreadable coordinate. Check the wire in EPLAN.", "eplan_connection_create", new() { ["page"] = scene.Page.Page });
                }
            }
        }
        // Owner 2026-11-16: the write-time layout rules over the whole page, so old, hand-drawn and api_try objects are checked too.
        string[] guarded = [.. _guardChecks.Keys.Where(Enabled)];
        if (guarded.Length > 0)
        {
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutGuard.Violation v in GuardViolations(scene))
            {
                if (!_guardRules.TryGetValue(v.Rule, out string? check) || !guarded.Contains(check) || v.Message.StartsWith("R4 unverified", StringComparison.Ordinal) || v.Rule == "R4" && EndsInside(scene, v))
                { continue; }
                // Every placement and wire is "changed" here, so a pair can be found from both sides.
                if (!reported.Add(v.Rule + ":" + string.Join(",", v.Objects.Select(o => FormattableString.Invariant($"{o.Type}/{o.Oid}")).OrderBy(s => s, StringComparer.Ordinal))))
                { continue; }
                DrawingLocator[] objects = [.. v.Objects.Where(o => o.Oid > 0)];
                double[] at = v.Geometry.Length == 4 ? [(v.Geometry[0] + v.Geometry[2]) / 2, (v.Geometry[1] + v.Geometry[3]) / 2] : v.Geometry;
                bool text = v.Rule is "R8" or "R9";
                Add(_guardChecks[check], v.Rule == "R5" || v.Rule == "R4" ? "error" : "warning", at, objects, v.Message,
                    text ? "eplan_placement_update" : "eplan_connection_create", text && objects.Length > 0 ? new() { ["locator"] = BridgeJson.FromObject(objects[0]) } : new() { ["page"] = scene.Page.Page });
            }
        }
        // MX_Dryer16 2026-11-08: cable texts ran across the +M location box border; the official pages keep them inside the box.
        // The box's own texts (its +M label sits on the corner) do not count.
        if (Enabled("box_crossings"))
        {
            static bool Area(double[]? r) => r is { Length: 4 } && r[0] < r[2] && r[1] < r[3];
            DrawingPlacement[] boxes = [.. scene.Page.Placements.Where(b => b.Kind == "LocationBox" && Area(b.Bounds))];
            foreach (DrawingPlacement text in scene.Page.Placements.Where(t => t.Kind is "Text" or "PropertyPlacement" && Area(t.Bounds)))
            {
                double[] a = text.Bounds!;
                foreach (DrawingPlacement box in boxes.Where(b => text.Parent is not { } parent || parent.Oid != b.Locator.Oid || parent.Type != b.Locator.Type))
                {
                    double[] b = box.Bounds!;
                    bool touches = a[0] < b[2] - 1e-6 && a[2] > b[0] + 1e-6 && a[1] < b[3] - 1e-6 && a[3] > b[1] + 1e-6;
                    bool inside = a[0] >= b[0] - 1e-6 && a[2] <= b[2] + 1e-6 && a[1] >= b[1] - 1e-6 && a[3] <= b[3] + 1e-6;
                    if (touches && !inside)
                    { Add("text_crosses_box", "warning", text.Location, [text.Locator, box.Locator], "Text runs across a location box border; keep it inside the box, as the official pages do, or outside it.", "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(text.Locator) }); }
                }
            }
        }
        string End(JToken? e)
        { JObject? endpoint = e as JObject; string? name = (string?)endpoint?["device"]; string? resolved = actual["devices"]?.Select(d => (string?)d["tag"]).FirstOrDefault(n => n is not null && name is not null && DrawingNames.SameName(name, n, scene.Page.Page)); return new JArray(resolved ?? name ?? "", (string?)endpoint?["pin"] ?? "").ToString(Newtonsoft.Json.Formatting.None); }
        bool SameEnds(JToken a, JToken b) => new HashSet<string>(new[] { End(a["from"]), End(a["to"]) }, StringComparer.Ordinal).SetEquals(new[] { End(b["from"]), End(b["to"]) });
        if (Enabled("unintended_junctions") && targetsByKind?["connections"] is JArray targets)
        {
            var networks = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (JToken c in targets)
            {
                string a = End(c["from"]), b = End(c["to"]);
                var joined = new HashSet<string>((networks.TryGetValue(a, out var first) ? first : [a]).Concat(networks.TryGetValue(b, out var second) ? second : [b]), StringComparer.Ordinal);
                foreach (string key in joined)
                { networks[key] = joined; }
            }
            foreach (JToken c in actual["connections"]!)
            {
                string a = End(c["from"]), b = End(c["to"]);
                if (networks.TryGetValue(a, out var group) && networks.ContainsKey(b) && !group.Contains(b))
                { Add("unintended_junction", "error", [], c["locator"] is JObject l ? [l.ToObject<DrawingLocator>()!] : [], "A logical connection joins separate target networks.", "eplan_placement_update", new() { ["page"] = scene.Page.Page }); }
            }
        }
        if (Enabled("interruption_pairing"))
        {
            foreach (var group in actual["interruptions"]!.GroupBy(p => (string?)p["name"], StringComparer.Ordinal))
            {
                JToken[] ordered = group.OrderBy(p => (int)p["pageOrder"]!).ThenBy(p => (double)p["position"]![0]!).ThenBy(p => (double)p["position"]![1]!).ToArray();
                for (int i = 0; i < ordered.Length; i++)
                {
                    JToken point = ordered[i];
                    if ((string?)point["page"] != scene.Page.Page)
                    { continue; }
                    JToken? wanted = targetsByKind?["interruptions"]?.FirstOrDefault(p => (string?)p["name"] == (string?)point["name"] && Near(p["position"]!.ToObject<double[]>()!, point["position"]!.ToObject<double[]>()!));
                    JToken? peer = (i ^ 1) < ordered.Length ? ordered[i ^ 1] : null;
                    if (wanted?["counterpart"] is JObject expected && (peer is null || (string?)expected["page"] != (string?)peer["page"] || !Near(expected["position"]!.ToObject<double[]>()!, peer["position"]!.ToObject<double[]>()!)))
                    { Add("interruption_pairing", "error", point["position"]!.ToObject<double[]>()!, [point["locator"]!.ToObject<DrawingLocator>()!], "A65 adjacent pairing differs from the target counterpart.", "eplan_placement_update", new() { ["locator"] = point["locator"]!.DeepClone() }); }
                }
            }
        }
        if (Enabled("cable_cores") && targetsByKind?["cables"] is JArray cables)
        {
            foreach (JToken cable in cables)
            {
                JToken? found = actual["cables"]!.FirstOrDefault(c => CableDefinitionPlanner.SameName((string)cable["designation"]!, (string)c["designation"]!, scene.Page.Page));
                foreach (JToken core in cable["cores"]!)
                {
                    JToken? wire = found?["cores"]?.FirstOrDefault(w => SameEnds(core, w));
                    if (wire is null || _coreFields.Any(k => core[k] is not null && !JToken.DeepEquals(core[k], wire[k])) || actual["connections"]!.Concat(found!["cores"]!).Any(w => !SameEnds(w, core) && w["definitionPoints"]!.Values<long>().Intersect(wire["definitionPoints"]!.Values<long>()).Any() && !cable["cores"]!.Any(t => SameEnds(t, w) && _coreFields.All(k => JToken.DeepEquals(t[k], core[k])))))
                    {
                        // A73 (live 2026-11-02): moving the cable puts it on the branch but drops its core assignment and
                        // generation does not restore it, so the computed position is advice, never a move fix.
                        JObject move = PageReviewCableFix.Arguments(scene.Page.GridSize, found, actual["connections"]!.FirstOrDefault(c => SameEnds(core, c)), (JArray)actual["connections"]!);
                        double[] target = move["moves"] is JArray { Count: 1 } moves ? [(double)moves[0]!["x_mm"]!, (double)moves[0]!["y_mm"]!] : [];
                        string message = target.Length == 2
                            ? FormattableString.Invariant($"Core membership differs; an A64 trunk segment shares its definition point across branches. The cable should cross the intended branch with its position at ({target[0]}, {target[1]}). Moving a cable drops its core assignment (A73) and no tool reassigns cores yet: draw the cable again across that branch with eplan_connection_create.")
                            : "Core membership differs. A parallel exclusive branch and complete crossing evidence are required for coordinates; redraw the cable across the intended branch.";
                        Add("cable_core_segment", "error", target, wire?["locator"] is JObject l ? [l.ToObject<DrawingLocator>()!] : [], message, "eplan_connection_create", new JObject());
                    }
                }
            }
        }
        if (Enabled("eplan_messages"))
        { foreach (JToken message in actual["messages"]!) { Add("eplan_message", (string)message["severity"]!, [], message["objects"]!.ToObject<DrawingLocator[]>()!, (string)message["text"]!, "eplan_check_list", new() { ["project"] = scene.Project.Name }); } }
        return issues;
    }
}
