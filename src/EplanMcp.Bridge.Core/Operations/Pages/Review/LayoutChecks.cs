using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The layout guard of the drawing tools, run over a whole page as it was measured.</summary>
internal static class ReviewGuard
{
    /// <summary>
    /// The layout guard over every placement and wire of the page, as measured: nets come from shared wire ends, polylines
    /// are split into segments, and no free position is searched.
    /// </summary>
    public static List<LayoutGuard.Violation> Violations(PageScene scene) =>
        LayoutGuard.Check(Model(scene, linesChanged: true), [.. scene.Page.Placements.Where(LayoutGuard.Positioned)], suggest: false, measured: true, searchViolations: false);

    public static LayoutGuard.Model Model(PageScene scene, bool linesChanged)
    {
        IEnumerable<LayoutGuard.Segment> segments = scene.Graphical.Select((s, i) => (s, i)).Where(x => x.s.Length >= 2).SelectMany(x => x.s.Zip(x.s.Skip(1), (a, b) => new LayoutGuard.Segment
        { Points = [a, b], Locator = new() { Oid = -x.i - 1 }, Changed = linesChanged }))
            // A point the reader could not measure is no geometry to check: the segment is left out, not reported.
            .Where(s => s.Points.All(p => p.Length == 2 && !p.Any(n => double.IsNaN(n) || double.IsInfinity(n))) && !s.Points[0].SequenceEqual(s.Points[1]));
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
}

/// <summary>
/// off_grid for placements and overlaps, in one pass over the placements: each placement's grid note comes before its
/// overlaps, and that order is part of the result.
/// </summary>
internal static class PlacementGridAndOverlapCheck
{
    // One page review shares this search time, like the drawing tools (LayoutGuard.FreePositionSearchBudget).
    private static readonly TimeSpan _freeSearchBudget = TimeSpan.FromSeconds(2);

    public static void Run(ReviewContext c)
    {
        PageScene scene = c.Scene;
        LayoutGuard.Model? guardModel = null;
        var searchTimer = System.Diagnostics.Stopwatch.StartNew();
        bool Expired() => searchTimer.Elapsed >= _freeSearchBudget;
        foreach (DrawingPlacement p in c.Placements)
        {
            if (c.Enabled("off_grid") && scene.Page.GridSize > 0 && p.Location.Any(v => Math.Abs(v / scene.Page.GridSize - Math.Round(v / scene.Page.GridSize)) > 1e-8))
            { c.Add("off_grid", "info", p.Location, [p.Locator], "Placement is off the page grid; library pin offsets are exempt.", "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(p.Locator), ["x"] = Math.Round(p.Location[0] / scene.Page.GridSize) * scene.Page.GridSize, ["y"] = Math.Round(p.Location[1] / scene.Page.GridSize) * scene.Page.GridSize }); }
            if (!c.Enabled("overlaps") || p.Bounds is not { Length: 4 } a || a[0] == a[2] || a[1] == a[3])
            { continue; }
            foreach (DrawingPlacement q in c.Placements.SkipWhile(q => !ReferenceEquals(q, p)).Skip(1))
            {
                // Agent eval 2026-11-06: PLC boxes 24 mm apart touch; EPLAN read one edge as 235.9999999999999.
                if (q.Bounds is { Length: 4 } b && a[0] < b[2] - 1e-6 && a[2] > b[0] + 1e-6 && a[1] < b[3] - 1e-6 && a[3] > b[1] + 1e-6 && (p.Properties.ContainsKey("name") || p.Kind == "Text") && (q.Properties.ContainsKey("name") || q.Kind == "Text"))
                {
                    bool symbols = OneSymbolOnAnother(c, p, q, a, b);
                    // Owner 2026-11-16: an error carries the nearest free grid position, so a repair needs no trial moves.
                    double[]? free = symbols ? FreePosition(scene, ref guardModel, q, Expired) : null;
                    var move = new JObject { ["locator"] = BridgeJson.FromObject(q.Locator) };
                    if (free is not null)
                    { move["x"] = free[0]; move["y"] = free[1]; }
                    c.Add("overlap", symbols ? "error" : "warning", p.Location, [p.Locator, q.Locator], !symbols ? "Symbol or text bounds overlap."
                        : free is null ? "Symbol outlines overlap; no free grid position was found nearby, move it by hand." : FormattableString.Invariant($"Symbol outlines overlap; the nearest free position for the second symbol is [{free[0]}, {free[1]}] mm."), "eplan_placement_update", move);
                }
            }
        }
    }

    /// <summary>
    /// Owner decision 2026-11-16: one symbol on another is a drawing error; overlapping texts stay a warning. What the
    /// official drawings do is no error (A147 and the live runs of 2026-11-16): a device encloses its own connection points
    /// (-TB1, -TB1:13); a graphic block (the card picture) lies inside a PLC box; parts of one device and its sub-devices
    /// share an outline; mated plugs, plug pins on a box edge and a breaker with its potential point touch by 0.25-1 mm.
    /// Outlines that overlap by half a grid both ways sit on each other.
    /// </summary>
    private static bool OneSymbolOnAnother(ReviewContext c, DrawingPlacement p, DrawingPlacement q, double[] a, double[] b) =>
        c.Schematic && p.Kind is not ("Text" or "Block") && q.Kind is not ("Text" or "Block") && !LayoutGuard.Related(p, q, c.Scene.Page.Placements) && !SameDevice(p, q)
        && Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]) >= Deep(c.Scene) && Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]) >= Deep(c.Scene);

    private static double Deep(PageScene scene) => Math.Max(1, scene.Page.GridSize / 2);

    private static string Device(DrawingPlacement p) => p.Properties.TryGetValue("name", out string? name) ? name.Split(':')[0] : "";

    /// <summary>One device, or a device and its sub-device (live 2026-11-16: the pins of -KK1-X11 sit on the -KK1 box edge).</summary>
    private static bool SameDevice(DrawingPlacement p, DrawingPlacement q)
    {
        string a = Device(p), b = Device(q);
        return a.Length > 0 && b.Length > 0 && (a == b || a.StartsWith(b + "-", StringComparison.Ordinal) || b.StartsWith(a + "-", StringComparison.Ordinal));
    }

    /// <summary>
    /// The nearest grid position where the symbol and its texts clear every rule, as the drawing tools search it; null when
    /// none is found or the shared search time is used up. Existing wires count as obstacles, not as changes.
    /// </summary>
    private static double[]? FreePosition(PageScene scene, ref LayoutGuard.Model? model, DrawingPlacement symbol, Func<bool> expired)
    {
        if (expired() || !LayoutGuard.Positioned(symbol))
        { return null; }
        model ??= ReviewGuard.Model(scene, linesChanged: false);
        DrawingPlacement[] group = [symbol, .. scene.Page.Placements.Where(t => t.Parent is { } owner && owner.Oid == symbol.Locator.Oid && owner.Type == symbol.Locator.Type && LayoutGuard.Positioned(t))];
        // The position must also pass eplan_placement_update's own move rule, or the suggested fix is refused (live 2026-11-16).
        return LayoutGuard.FindFree(model, symbol, group, measured: true, expired: expired,
            accept: trial => LayoutGuard.ClearOfOthers([.. scene.Page.Placements.Where(p => !group.Contains(p)), .. trial], trial));
    }
}

/// <summary>off_area: an outline or a wire point outside the drawing area.</summary>
internal static class OffAreaCheck
{
    public static void Run(ReviewContext c)
    {
        PageScene scene = c.Scene;
        // A145: the plot frame's evaluation grid keeps the title block, border and column header clear; without it the page edge.
        double[]? area = scene.Page.DrawingArea ?? (scene.Page.PageSize is { Length: 2 } size ? [0, 0, size[0], size[1]] : null);
        if (!c.Enabled("off_area") || area is null)
        { return; }
        string where = scene.Page.DrawingArea is null ? "the page" : "the plot frame's drawing area";
        double Shift(double low, double high, double min, double max) => low < min - 1e-6 ? min - low : high > max + 1e-6 ? max - high : 0;
        double Snap(double v, double toward) => toward != 0 && scene.Page.GridSize > 0 ? (toward > 0 ? Math.Ceiling(v / scene.Page.GridSize - 1e-9) : Math.Floor(v / scene.Page.GridSize + 1e-9)) * scene.Page.GridSize : v;
        foreach (DrawingPlacement p in c.Placements.Where(p => p.Bounds is { Length: 4 } b && b.All(v => !double.IsNaN(v) && !double.IsInfinity(v)) && !b.All(v => v == 0)))
        {
            double[] b = p.Bounds!;
            double dx = Shift(b[0], b[2], area[0], area[2]), dy = Shift(b[1], b[3], area[1], area[3]);
            if (dx == 0 && dy == 0)
            { continue; }
            // A text that belongs to a symbol moves with it; the fix moves the owner.
            DrawingLocator target = p.Parent is { } owner && p.Kind != "Text" ? owner : p.Locator;
            double[] location = scene.Page.Placements.FirstOrDefault(o => o.Locator.Oid == target.Oid && o.Locator.Type == target.Type && o.Parent is null)?.Location ?? p.Location;
            // A147: EPLAN reports property texts longer than their ink along the text direction (official HVAC pages: ink inside
            // the area, outline up to 45 mm past it), and macro box frames and loose graphics are not symbols.
            bool symbol = p.Kind is not ("PropertyPlacement" or "Text" or "MacroBox") && (p.Properties.ContainsKey("name") || LayoutGuard.Container(p));
            c.Add("off_area", symbol ? "error" : "warning", p.Location, [p.Locator], FormattableString.Invariant($"Outline [{b[0]}, {b[1]}, {b[2]}, {b[3]}] leaves {where} [{area[0]}, {area[1]}, {area[2]}, {area[3]}]; keep it clear of the border and title block."),
                "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(target), ["x"] = Snap(location[0] + dx, dx), ["y"] = Snap(location[1] + dy, dy) });
        }
        foreach (double[] point in c.Graphical.SelectMany(s => s).Where(ReviewContext.Known).Where(p => p[0] != 0 || p[1] != 0).GroupBy(p => string.Join("/", p)).Select(g => g.First()))
        {
            if (point[0] < area[0] - 1e-6 || point[1] < area[1] - 1e-6 || point[0] > area[2] + 1e-6 || point[1] > area[3] + 1e-6)
            {
                // Live 2026-11-16 (official =EC1+A3&EFS1/1): an invisible wire point at (8, -4) lies off the page, like the A73
                // default coordinates; the drawing tools refuse off-page wires (ADR-0046 S2), so a point off the page only warns.
                bool onPage = scene.Page.PageSize is not { Length: 2 } s || point[0] >= -1e-6 && point[1] >= -1e-6 && point[0] <= s[0] + 1e-6 && point[1] <= s[1] + 1e-6;
                c.Add("off_area", onPage ? "error" : "warning", point, [], onPage
                    ? FormattableString.Invariant($"Wire point leaves {where}; reroute it inside [{area[0]}, {area[1]}, {area[2]}, {area[3]}].")
                    : "Wire point lies off the page and is not drawn; it may be an unreadable coordinate. Check the wire in EPLAN.", "eplan_connection_create", new() { ["page"] = scene.Page.Page });
            }
        }
    }
}

/// <summary>
/// wire_through_symbol, net_overlap, text_format and cross_reference_clash: the write-time layout rules R4, R5, R6, R8
/// and R9 over the whole page, so old, hand-drawn and api_try objects are checked too (owner 2026-11-16).
/// </summary>
internal static class LayoutRuleChecks
{
    // Review check name -> issue code, and layout rule -> review check.
    private static readonly Dictionary<string, string> _guardChecks = new(StringComparer.Ordinal)
    { ["wire_through_symbol"] = "wire_through_symbol", ["net_overlap"] = "net_overlap", ["text_format"] = "text_format", ["cross_reference_clash"] = "cross_reference_clash" };
    private static readonly Dictionary<string, string> _guardRules = new(StringComparer.Ordinal)
    { ["R4"] = "wire_through_symbol", ["R5"] = "net_overlap", ["R6"] = "net_overlap", ["R8"] = "text_format", ["R9"] = "cross_reference_clash" };

    public static void Run(ReviewContext c)
    {
        PageScene scene = c.Scene;
        // Model view pages are projections of the 3D layout and P&ID pages draw pipes and plant pictures: their outlines
        // cross by design (hundreds of findings on the official &ETC1 pages, A147), so the schematic rules do not apply.
        string[] guarded = !c.Schematic ? [] : [.. _guardChecks.Keys.Where(c.Enabled)];
        if (guarded.Length == 0)
        { return; }
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (LayoutGuard.Violation v in ReviewGuard.Violations(scene))
        {
            if (!_guardRules.TryGetValue(v.Rule, out string? check) || !guarded.Contains(check) || v.Message.StartsWith("R4 unverified", StringComparison.Ordinal) || v.Rule == "R4" && EndsInside(scene, v))
            { continue; }
            // Every placement and wire is "changed" here, so a pair can be found from both sides.
            if (!reported.Add(v.Rule + ":" + string.Join(",", v.Objects.Select(o => FormattableString.Invariant($"{o.Type}/{o.Oid}")).OrderBy(s => s, StringComparer.Ordinal))))
            { continue; }
            DrawingLocator[] objects = [.. v.Objects.Where(o => o.Oid > 0)];
            double[] at = v.Geometry.Length == 4 ? [(v.Geometry[0] + v.Geometry[2]) / 2, (v.Geometry[1] + v.Geometry[3]) / 2] : v.Geometry;
            bool text = v.Rule is "R8" or "R9";
            c.Add(_guardChecks[check], v.Rule == "R5" || v.Rule == "R4" ? "error" : "warning", at, objects, v.Message,
                text ? "eplan_placement_update" : "eplan_connection_create", text && objects.Length > 0 ? new() { ["locator"] = BridgeJson.FromObject(objects[0]) } : new() { ["page"] = scene.Page.Page });
        }
    }

    /// <summary>
    /// Live 2026-11-16 (official =F+A2&amp;EFS1/1): PLC plug outlines of 44 x 34 mm cover the pins of their neighbours; a wire that
    /// ends on a pin inside the outline connects there and does not cross the symbol.
    /// </summary>
    private static bool EndsInside(PageScene scene, LayoutGuard.Violation v)
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
}
