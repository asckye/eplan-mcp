using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Detached, incremental layout validation. Existing defects outside the proposed change do not block repairs.</summary>
public static class LayoutGuard
{
    public interface IMeasurement { public Model ReadLayout(); }
    public sealed class Options
    {
        public double SymbolClearanceMm { get; set; }
        public double? ParallelSpacingMm { get; set; }
        public double MinimumTextHeightMm { get; set; } = 0.5;
        public double MarginMm { get; set; } = 10;
        public bool Strict { get; set; }
    }
    public sealed class Model
    {
        public PageDrawingGeometry Page { get; set; } = new();
        public double[]? DrawingArea { get; set; }
        public Options Config { get; set; } = new();
        public List<Segment> Lines { get; set; } = [];
        public Dictionary<string, SymbolConnectionPoint> Pins { get; set; } = [];
        // Connection definition points annotate an owning route, rather than ending it at a directed pin.
        public Dictionary<string, List<double[][]>> PointRoutes { get; set; } = [];
        public Dictionary<string, string> PointRouteGaps { get; set; } = [];
        [Newtonsoft.Json.JsonIgnore]
        public Func<Model>? ReadActual { get; set; }
        // Attribute-level intent survives measurement without treating symbol defaults as requested formatting.
        [Newtonsoft.Json.JsonIgnore]
        public Func<DrawingPlacement, string, bool>? SetsTextAttribute { get; set; }
    }
    public sealed class Segment
    {
        public DrawingLocator Locator { get; set; } = new();
        public double[][] Points { get; set; } = [];
        public string? Net { get; set; }
        public bool Changed { get; set; }
    }
    public sealed class Violation
    {
        public string Rule { get; set; } = "";
        public DrawingLocator[] Objects { get; set; } = [];
        public double[] Geometry { get; set; } = [];
        public double[]? NearestFreePosition { get; set; }
        public bool Warning { get; set; }
        public string Message { get; set; } = "";
    }
    public static double[] Area(Model model)
    {
        Options c = model.Config;
        if (new[] { c.SymbolClearanceMm, c.ParallelSpacingMm ?? model.Page.GridSize, c.MinimumTextHeightMm, c.MarginMm }.Any(n => !Finite(n) || n < 0)
            || !Finite(model.Page.GridSize) || model.Page.GridSize <= 0 || c.MinimumTextHeightMm <= 0)
        { throw WriteArguments.Invalid(["layout: use finite nonnegative distances, positive text height and page grid."]); }
        double[]? area = model.DrawingArea ?? (model.Page.PageSize is { Length: 2 } s ? [c.MarginMm, c.MarginMm, s[0] - c.MarginMm, s[1] - c.MarginMm] : null);
        return DrawingCoordinates.ValidBounds(area) && area![0] < area[2] && area[1] < area[3] ? area
            : throw new BridgeErrorException(ErrorReason.CapabilityMissing, "R1: reliable drawing area unavailable; read page geometry before drawing.");
    }
    public static List<Violation> Check(Model model, IReadOnlyList<DrawingPlacement> changed, bool suggest = true, bool measured = false)
    {
        double[] area = Area(model);
        var result = new List<Violation>();
        DrawingPlacement[] all = [.. model.Page.Placements.Where(p => !changed.Any(c => Same(c, p))), .. changed];
        void Add(string rule, DrawingPlacement p, DrawingLocator? other, string message, bool warningOnly = false)
        {
            bool warning = warningOnly || !model.Config.Strict && rule is "R6" or "R7" or "R9";
            result.Add(new()
            {
                Rule = rule,
                Objects = other is null ? [p.Locator] : [p.Locator, other],
                Geometry = p.Bounds ?? p.Location,
                Warning = warning,
                Message = rule + ": " + (warning ? "Warning: " : "") + message + " " + (model.DrawingArea is null ? "Boundary fallback: Page.Size minus layout.marginMm; plot-frame usable area is unverified. " : "") + (warningOnly ? "Review the symbol's text layout." : "Choose another grid position or reroute.")
            });
        }
        foreach (DrawingPlacement p in changed)
        {
            if (!Positioned(p))
            { continue; }
            double[] b = DrawingCoordinates.ValidBounds(p.Bounds) ? p.Bounds! : [p.Location[0], p.Location[1], p.Location[0], p.Location[1]];
            if (b[0] < area[0] || b[1] < area[1] || b[2] > area[2] || b[3] > area[3]
                || p.Location[0] < area[0] || p.Location[1] < area[1] || p.Location[0] > area[2] || p.Location[1] > area[3])
            { Add("R1", p, null, "Keep the full outline inside the drawing area."); }
            if (p.Parent is null && p.Location.Any(n => !OnGrid(n, model.Page.GridSize)))
            { Add("R7", p, null, "Placement points and bends must lie on the page grid; pins are exempt."); }
            // A text whose height or angle EPLAN does not report is not a violation: only a reported bad value refuses.
            bool badHeight = Height(p, out double height) && height < model.Config.MinimumTextHeightMm;
            bool badAngle = Angle(p, out double angle) && Math.Abs(angle - Math.Round(angle / 90) * 90) > 1e-6;
            if (Text(p) && (badHeight || badAngle))
            {
                bool requested = badHeight && model.SetsTextAttribute?.Invoke(p, "height_mm") == true
                    || badAngle && model.SetsTextAttribute?.Invoke(p, "angle") == true;
                Add("R8", p, null, "Use the configured minimum text height and rotation 0, 90, 180 or 270 degrees.", !requested);
            }
            foreach (DrawingPlacement q in all.Where(q => !ReferenceEquals(p, q) && !Same(p, q)))
            {
                if (!Positioned(q) || !DrawingCoordinates.ValidBounds(q.Bounds))
                { continue; }
                bool overlap = Overlaps(b, q.Bounds!);
                if (Related(p, q, all))
                {
                    if (SameOwner(p, q) && (Text(p) || Text(q)) && overlap)
                    { Add("R3", p, q.Locator, "Text overlaps within the same owner; symbol-default positions may need adjustment.", true); }
                    continue;
                }
                if ((Symbol(p) || Container(p)) && (Symbol(q) || Container(q)) && (Overlaps(b, q.Bounds!, model.Config.SymbolClearanceMm) || p.Bounds is null && p.Location.SequenceEqual(q.Location)))
                { Add("R2", p, q.Locator, "A symbol outline conflicts with another placement. A moved outline conflicts with another placement."); }
                if ((Text(p) && (Text(q) || Symbol(q) || Container(q) || q.Kind == "Line") || Text(q) && (Symbol(p) || Container(p) || p.Kind == "Line")) && overlap)
                { Add("R3", p, q.Locator, "Text bounds conflict with another text, symbol or line."); }
                if (q.Properties.TryGetValue("cross_reference", out string? cross) && cross == "true" && overlap)
                { Add("R9", p, q.Locator, "Keep clear of existing cross-reference text."); }
            }
        }
        var unverifiedPoints = new HashSet<string>(StringComparer.Ordinal);
        foreach (Segment line in model.Lines)
        {
            double[] box = Box(line.Points);
            var row = new DrawingPlacement { Locator = line.Locator, Kind = "Line", Location = line.Points[0], Bounds = box };
            if (line.Changed && (box[0] < area[0] || box[1] < area[1] || box[2] > area[2] || box[3] > area[3]))
            { Add("R1", row, null, "Keep the complete line inside the drawing area."); }
            foreach (DrawingPlacement p in all.Where(p => line.Changed || changed.Contains(p)))
            {
                if (!Positioned(p) || !DrawingCoordinates.ValidBounds(p.Bounds))
                { continue; }
                if (Symbol(p))
                {
                    if (Crosses(line.Points, p, model.Pins, model.PointRoutes, out bool unverified))
                    { Add("R4", p, line.Locator, "The path crosses the outline of a symbol; a moved outline conflicts with an autoconnect line."); }
                    if (unverified)
                    {
                        string owner = FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}");
                        if (unverifiedPoints.Add(owner))
                        {
                            string reason = model.PointRouteGaps.TryGetValue(owner, out string? gap) ? gap : "route evidence unavailable";
                            result.Add(new()
                            {
                                Rule = "R4",
                                Objects = [p.Locator],
                                Geometry = p.Bounds!,
                                Warning = true,
                                Message = $"R4 unverified: connection definition point {owner} has no readable owning route ({reason})"
                            });
                        }
                    }
                }
                if (Text(p) && !Related(p, row, all) && Hits(line.Points, p.Bounds!))
                { Add("R3", p, line.Locator, "Text bounds conflict with a line."); }
            }
            if (!line.Changed)
            { continue; }
            if (line.Points.Any(p => !model.Pins.Values.Any(pin => pin.Location.SequenceEqual(p)) && p.Any(n => !OnGrid(n, model.Page.GridSize))))
            { Add("R7", row, null, "Line bends must lie on the page grid; pin endpoints are exempt."); }
            foreach (Segment other in model.Lines.Where(s => !ReferenceEquals(s, line) && (line.Net is null || s.Net is null || line.Net != s.Net)))
            {
                double[] b = Box(other.Points);
                bool horizontal = line.Points[0][1] == line.Points[1][1], vertical = line.Points[0][0] == line.Points[1][0];
                if (!(horizontal && other.Points[0][1] == other.Points[1][1] || vertical && other.Points[0][0] == other.Points[1][0]))
                { continue; }
                int axis = horizontal ? 0 : 1;
                double distance = Math.Abs(box[1 - axis] - b[1 - axis]);
                if (box[axis] < b[axis + 2] && box[axis + 2] > b[axis] && (distance == 0 || distance < (model.Config.ParallelSpacingMm ?? model.Page.GridSize)))
                { Add(distance == 0 ? "R5" : "R6", row, other.Locator, "Different nets have collinear overlap or insufficient parallel spacing; reroute the segments."); }
            }
        }
        if (suggest)
        {
            foreach (DrawingPlacement p in changed.Where(p => result.Any(v => !v.Warning && v.Objects[0] == p.Locator)))
            {
                DrawingPlacement owner = GroupOwner(p, changed);
                double[]? free = FindFree(model, owner, changed, measured: measured);
                foreach (Violation v in result.Where(v => !v.Warning && v.Objects[0] == p.Locator))
                { v.NearestFreePosition = free; v.Message += free is null ? " No free position found within the bounded search." : FormattableString.Invariant($" Suggested position: [{free[0]}, {free[1]}] mm."); }
            }
        }
        return result;
    }
    public static double[]? FindFree(Model model, DrawingPlacement item, IReadOnlyList<DrawingPlacement> changed, int radius = 12, bool measured = false)
    {
        radius = Math.Min(12, Math.Max(0, radius));
        double g = model.Page.GridSize;
        var search = new Model
        {
            Page = model.Page,
            DrawingArea = model.DrawingArea,
            Config = model.Config,
            Pins = model.Pins,
            PointRoutes = model.PointRoutes,
            PointRouteGaps = model.PointRouteGaps,
            Lines = model.Lines,
            SetsTextAttribute = (row, attribute) => changed.Any(p => Same(p, row) && model.SetsTextAttribute?.Invoke(p, attribute) == true)
        };
        var offsets = Enumerable.Range(-radius, 2 * radius + 1).SelectMany(x => Enumerable.Range(-radius, 2 * radius + 1).Select(y => new[] { x, y }))
            .OrderBy(p => p[0] * p[0] + p[1] * p[1]).ThenBy(p => p[0]).ThenBy(p => p[1]);
        DrawingPlacement[] closure = changed.Where(p => Same(p, item) || Same(GroupOwner(p, changed), item)).ToArray();
        foreach (int[] offset in offsets)
        {
            double[] point = item.Location.Select((n, i) => Math.Round(n / g, MidpointRounding.AwayFromZero) * g + offset[i] * g).ToArray();
            double[] delta = point.Zip(item.Location, (a, b) => a - b).ToArray();
            DrawingPlacement[] trial = changed.Except(closure).Concat(closure.Select(p => Shift(p, delta))).ToArray();
            if (!Check(search, trial, false, measured).Any(v => !v.Warning || v.Rule is "R7" or "R9"))
            { return point; }
        }
        return null;
    }
    public static List<string> Enforce(Model model, IReadOnlyList<DrawingPlacement> changed, string at = "", bool measured = false)
    {
        List<Violation> violations = Check(model, changed, measured: measured);
        if (violations.Any(v => !v.Warning))
        {
            BridgeErrorException error = WriteArguments.Invalid(violations.Where(v => !v.Warning).Select(v => at + v.Message).ToList());
            error.Error.Metadata!["layoutViolations"] = Newtonsoft.Json.JsonConvert.SerializeObject(violations);
            error.Error.Metadata["layoutChecks"] = Report(model, false) + (measured ? "\n" + Report(model, true) : "") + "\n" + Notes(model.Page.Placements.Concat(changed));
            throw WriteArguments.NotStarted(error);
        }
        return [Report(model, measured), .. new[] { Notes(model.Page.Placements.Concat(changed)) }.Where(s => s.Length != 0), .. violations.Select(v => v.Message)];
    }
    public static string Report(Model model, bool measured) => (measured
        ? "Layout after write: R1-R9 on measured bounds, text metadata and routes. "
        : "Layout before write: R1/R7 insertion points; R1-R9 on known bounds, text metadata and routes; unknown bounds deferred. ")
        + (model.DrawingArea is null ? "Boundary fallback: Page.Size minus layout.marginMm." : "Boundary: plot-frame drawing area.");
    public static string Summary(IEnumerable<string?> messages) => string.Join("\n", messages.Where(s => !string.IsNullOrEmpty(s)).SelectMany(s => s!.Split('\n'))
        .Distinct(StringComparer.Ordinal).OrderBy(s => s.StartsWith("Layout ", StringComparison.Ordinal) ? 0 : 1));
    public static List<string> AfterWriting(Model before, Model? actual = null)
    {
        actual ??= before.ReadActual?.Invoke() ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Actual layout reader is unavailable; roll back the write.");
        actual.SetsTextAttribute = before.SetsTextAttribute;
        var previous = new HashSet<string>(before.Page.Placements.Select(Newtonsoft.Json.JsonConvert.SerializeObject), StringComparer.Ordinal);
        DrawingPlacement[] changed = actual.Page.Placements.Where(p => !previous.Contains(Newtonsoft.Json.JsonConvert.SerializeObject(p))).ToArray();
        // A54: empty property texts report all-zero bounds and have no visible ink; they are not unmeasured objects.
        if (changed.Any(p => Positioned(p) && !DrawingCoordinates.ValidBounds(p.Bounds)))
        { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Actual placed bounds could not be measured; roll back the write."); }
        string Point(double[] p) => string.Join(",", p.Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
        string Key(Segment s) => string.Join(";", s.Points.Select(Point).OrderBy(p => p, StringComparer.Ordinal));
        actual.Lines = actual.Lines.GroupBy(Key, StringComparer.Ordinal).Select(g => g.First()).ToList();
        var roots = actual.Lines.SelectMany(s => s.Points).Select(Point).Distinct(StringComparer.Ordinal).ToDictionary(p => p, p => p, StringComparer.Ordinal);
        string Root(string p)
        { while (roots[p] != p) { p = roots[p]; } return p; }
        foreach (Segment s in actual.Lines)
        { roots[Root(Point(s.Points[0]))] = Root(Point(s.Points[1])); }
        foreach (Segment s in actual.Lines)
        { s.Changed = !before.Lines.Any(b => Key(b) == Key(s)); s.Net = Root(Point(s.Points[0])); }
        return Enforce(actual, changed, measured: true);
    }
    public static DrawingPlacement Shift(DrawingPlacement p, double[] delta) => new()
    {
        Locator = p.Locator,
        Parent = p.Parent,
        Kind = p.Kind,
        Properties = p.Properties,
        Location = p.Location.Length == 2 && p.Location.All(Finite) ? p.Location.Select((n, i) => n + delta[i]).ToArray() : p.Location,
        Bounds = ZeroBounds(p) ? p.Bounds : p.Bounds?.Select((n, i) => n + delta[i % 2]).ToArray()
    };
    public static Model From(ConnectionCreatePlanner.Scene scene) => new() { Page = scene.Page, Config = scene.Layout, Pins = new(scene.Pins), PointRoutes = scene.PointRoutes, PointRouteGaps = scene.PointRouteGaps, Lines = scene.Graphical.Select((s, i) => new Segment { Points = s, Net = null, Locator = new() { Oid = -i - 1 } }).ToList() };
    public static DrawingPlacement Proposed(string kind, double[] location, double[]? bounds, int index = 0) => new() { Kind = kind, Location = location, Bounds = bounds, Locator = new() { Oid = -index - 1 } };
    public static bool Overlaps(double[] a, double[] b, double clearance = 0) => a[0] < b[2] + clearance && a[2] + clearance > b[0] && a[1] < b[3] + clearance && a[3] + clearance > b[1];
    private static bool Text(DrawingPlacement p) => p.Kind is "Text" or "PropertyPlacement";
    private static bool Container(DrawingPlacement p) => p.Properties.TryGetValue("layout_container", out string? value) && value == "true" || p.Kind is "LocationBox" or "PLC" or "PLCBox" or "Box";
    private static bool Symbol(DrawingPlacement p) => !Container(p) && (p.Kind is "Function" or "SymbolReference" or "PotentialDefinition" or "ConnectionDefinitionPoint" or "Macro" || p.Properties.ContainsKey("layout_symbol"));
    private static bool OwnerId(DrawingLocator a, DrawingLocator b) => ProjectResolver.SamePath(a.Project, b.Project) && a.Page == b.Page && a.Type == b.Type && a.Oid == b.Oid;
    private static bool SameOwner(DrawingPlacement p, DrawingPlacement q) => OwnerId(p.Parent ?? p.Locator, q.Parent ?? q.Locator);
    private static DrawingPlacement GroupOwner(DrawingPlacement item, IReadOnlyList<DrawingPlacement> changed)
    {
        var seen = new HashSet<DrawingPlacement> { item };
        while (item.Parent is { } parent && changed.FirstOrDefault(p => !Text(p) && OwnerId(p.Locator, parent)) is { } owner && seen.Add(owner))
        { item = owner; }
        return item;
    }
    internal static bool Related(DrawingPlacement p, DrawingPlacement q, IReadOnlyList<DrawingPlacement> all)
    {
        if (SameOwner(p, q))
        { return true; }
        DrawingPlacement Owner(DrawingPlacement row) => !Text(row) || row.Parent is null ? row : all.FirstOrDefault(r => !Text(r) && OwnerId(r.Locator, row.Parent)) ?? row;
        DrawingPlacement a = Owner(p), b = Owner(q);
        if (SameOwner(a, b) || ProjectResolver.SamePath(a.Locator.Project, b.Locator.Project) && a.Locator.Page == b.Locator.Page
            && a.Properties.TryGetValue("layout_container_owner", out string? owner) && owner.Length != 0
            && b.Properties.TryGetValue("layout_container_owner", out string? other) && owner == other)
        { return true; }
        bool Inside(DrawingPlacement box, DrawingPlacement item)
        {
            // Unknown predicted outlines are deferred to measurement, just like the page-boundary check.
            double[]? bounds = DrawingCoordinates.ValidBounds(item.Bounds) ? item.Bounds : item.Location.Length == 2
                ? [item.Location[0], item.Location[1], item.Location[0], item.Location[1]] : null;
            return Container(box) && DrawingCoordinates.ValidBounds(box.Bounds) && DrawingCoordinates.ValidBounds(bounds)
                && box.Bounds![0] <= bounds![0] + 1e-6 && box.Bounds[1] <= bounds[1] + 1e-6
                && box.Bounds[2] >= bounds[2] - 1e-6 && box.Bounds[3] >= bounds[3] - 1e-6;
        }
        return Inside(a, b) || Inside(b, a);
    }
    private static string Id(DrawingLocator p) => FormattableString.Invariant($"{p.Type}/{p.Oid}/{p.Digest}");
    private static bool Same(DrawingPlacement p, DrawingPlacement q) => Id(p.Locator) == Id(q.Locator);
    private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    private static bool Number(DrawingPlacement p, string key, out double n) { n = 0; return p.Properties.TryGetValue(key, out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && Finite(n); }
    private static bool Height(DrawingPlacement p, out double n) => Number(p, "height_mm", out n) && n > 0;
    private static bool Angle(DrawingPlacement p, out double n)
    {
        if (!Number(p, "angle", out n) || n == -16002)
        { return false; }
        n = (n % 360 + 360) % 360;
        return true;
    }
    private static bool ZeroBounds(DrawingPlacement p) => p.Bounds is { Length: 4 } && p.Bounds.All(n => n == 0);
    internal static bool Positioned(DrawingPlacement p) => p.Location.Length == 2 && p.Location.All(Finite) && !ZeroBounds(p);
    private static string Notes(IEnumerable<DrawingPlacement> placements)
    {
        DrawingPlacement[] rows = placements.ToArray();
        return Summary(new[]
        {
            rows.Any(p => Text(p) && (!Height(p, out _) || !Angle(p, out _))) ? "R8 unverified: unknown or from_layer text height/angle was not checked." : "",
            rows.Any(p => !Positioned(p)) ? "R1-R9 unverified: placements with unknown positions or all-zero bounds were not checked." : ""
        });
    }
    private static bool OnGrid(double n, double grid) => Finite(n) && Math.Abs(n / grid - Math.Round(n / grid)) < 1e-8;
    private static double[] Box(double[][] s) => [Math.Min(s[0][0], s[1][0]), Math.Min(s[0][1], s[1][1]), Math.Max(s[0][0], s[1][0]), Math.Max(s[0][1], s[1][1])];
    private static bool Hits(double[][] s, double[] b)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < 2; i++)
        {
            double d = s[1][i] - s[0][i];
            if (d == 0)
            { if (s[0][i] < b[i] || s[0][i] > b[i + 2]) { return false; } continue; }
            double a = (b[i] - s[0][i]) / d, z = (b[i + 2] - s[0][i]) / d;
            lo = Math.Max(lo, Math.Min(a, z));
            hi = Math.Min(hi, Math.Max(a, z));
        }
        return lo <= hi;
    }
    private static bool ConnectedPoint(double[][] segment, DrawingPlacement p, Dictionary<string, SymbolConnectionPoint> pins,
        Dictionary<string, List<double[][]>>? pointRoutes, out bool unverified)
    {
        unverified = false;
        string owner = FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}");
        if (p.Kind == "ConnectionDefinitionPoint")
        {
            // A75 fifth run: rebinding changes existing type 59 points even when their owning route is unreadable.
            // Unknown ownership only warns on centre passage; known perpendicular routes and outline clips still refuse.
            double[] centre = DrawingCoordinates.ValidBounds(p.Bounds)
                ? [(p.Bounds![0] + p.Bounds[2]) / 2, (p.Bounds[1] + p.Bounds[3]) / 2] : p.Location;
            double[] box = [centre[0], centre[1], centre[0], centre[1]];
            if (!Hits(segment, box))
            { return false; }
            if (pointRoutes?.TryGetValue(owner, out List<double[][]>? routes) != true)
            { unverified = true; return true; }
            return routes.Any(route => Hits(route, box)
                    && (segment[0][0] == segment[1][0] && route[0][0] == route[1][0]
                        || segment[0][1] == segment[1][1] && route[0][1] == route[1][1]));
        }
        SymbolConnectionPoint[] own = ConnectionNodeGeometry.PassThroughPins(p, pins);
        return own.Any(pin => segment.Any(end => end.SequenceEqual(pin.Location)) && segment.Any(other => Faces(pin, other)));
    }
    private static bool Faces(SymbolConnectionPoint pin, double[] other) => pin.Direction switch
    {
        "Up" => other[0] == pin.Location[0] && other[1] > pin.Location[1],
        "Down" => other[0] == pin.Location[0] && other[1] < pin.Location[1],
        "Left" => other[1] == pin.Location[1] && other[0] < pin.Location[0],
        "Right" => other[1] == pin.Location[1] && other[0] > pin.Location[0],
        _ => false
    };
    internal static bool Crosses(double[][] s, DrawingPlacement p, Dictionary<string, SymbolConnectionPoint> pins,
        Dictionary<string, List<double[][]>>? pointRoutes = null) => Crosses(s, p, pins, pointRoutes, out _);
    private static bool Crosses(double[][] s, DrawingPlacement p, Dictionary<string, SymbolConnectionPoint> pins,
        Dictionary<string, List<double[][]>>? pointRoutes, out bool unverified)
    {
        unverified = false;
        return Hits(s, p.Bounds!) && !ConnectedPoint(s, p, pins, pointRoutes, out unverified)
            && (p.Kind == "ConnectionDefinitionPoint" || !pins.Any(pin => pin.Key.StartsWith(FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}/"), StringComparison.Ordinal)
            && Hits([pin.Value.Location, pin.Value.Location], p.Bounds!)
            && s.Any(end => end.SequenceEqual(pin.Value.Location) && s.Any(other => !other.SequenceEqual(end) && !Hits([other, other], p.Bounds!)
                && Faces(pin.Value, other)))));
    }
}
