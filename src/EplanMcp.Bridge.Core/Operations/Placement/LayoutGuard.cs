using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Detached, incremental layout validation. Existing defects outside the proposed change do not block repairs.</summary>
public static partial class LayoutGuard
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
        public bool AllowUnknownGeometry { get; set; }
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
        /// <summary>For tests: how long the free-position search has run, in place of the wall clock.</summary>
        internal Func<TimeSpan>? SearchElapsed { get; set; }
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
        if (new[] { c.SymbolClearanceMm, c.ParallelSpacingMm ?? (model.AllowUnknownGeometry && (!Finite(model.Page.GridSize) || model.Page.GridSize <= 0) ? 0 : model.Page.GridSize), c.MinimumTextHeightMm, c.MarginMm }.Any(n => !Finite(n) || n < 0)
            || !model.AllowUnknownGeometry && (!Finite(model.Page.GridSize) || model.Page.GridSize <= 0) || c.MinimumTextHeightMm <= 0)
        { throw WriteArguments.Invalid(["layout: use finite nonnegative distances, positive text height and page grid."]); }
        return UsableArea(model.Page, FrameArea(model), c.MarginMm, model.AllowUnknownGeometry)
            ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "R1: reliable drawing area unavailable; read page geometry before drawing.");
    }
    internal static double[]? UsableArea(PageDrawingGeometry page, double[]? frame, double margin, bool allowUnknown)
    {
        double[]? area = frame ?? (page.PageSize is { Length: 2 } s && s.All(n => Finite(n) && n > 0)
            ? [margin, margin, s[0] - margin, s[1] - margin] : null);
        if (allowUnknown && area is null)
        { return [double.MinValue, double.MinValue, double.MaxValue, double.MaxValue]; }
        return DrawingCoordinates.ValidBounds(area) && area![0] < area[2] && area[1] < area[3] ? area : null;
    }
    /// <summary>The caller's area, else the plot frame's evaluation grid read with the page (A145).</summary>
    /// <summary>
    /// A145: page size over frame size. A scaled page (official =EC1+A3&amp;EFS1/1, 1.5) keeps its symbols and grid and is printed
    /// smaller, so only printed sizes such as the minimum text height scale with it. 1 when either size is unknown.
    /// </summary>
    public static double PrintScale(PageDrawingGeometry page) => page.PageSize is { Length: 2 } s && page.PlotFrameSize is { Length: 2 } f
        && Finite(s[0]) && Finite(f[0]) && s[0] > 0 && f[0] > 0 ? s[0] / f[0] : 1;
    public static double[]? FrameArea(Model model) => model.DrawingArea ?? model.Page.DrawingArea;
    public static List<Violation> Check(Model model, IReadOnlyList<DrawingPlacement> changed, bool suggest = true, bool measured = false, bool searchViolations = true, TimeSpan? searchBudget = null)
        => new Checking(model, changed, measured).Run(suggest, searchViolations, searchBudget ?? FreePositionSearchBudget);

    /// <summary>
    /// The move rule of eplan_placement_update: a moved outline may not touch an unrelated outline, edges included. page holds
    /// every placement at its predicted position, moved the ones that move; an unknown outline counts as its insertion point.
    /// </summary>
    public static bool ClearOfOthers(IReadOnlyList<DrawingPlacement> page, IReadOnlyList<DrawingPlacement> moved)
    {
        static double[] Box(DrawingPlacement p) => p.Bounds?.All(n => n == 0) != false ? [p.Location[0], p.Location[1], p.Location[0], p.Location[1]] : p.Bounds!;
        var owners = new Owners(page);
        foreach (DrawingPlacement m in moved)
        {
            double[] a = Box(m);
            if (page.Any(p => Positioned(p) && !Same(p, m) && !Related(p, m, owners) && Box(p) is var b && a[0] <= b[2] && a[2] >= b[0] && a[1] <= b[3] && a[3] >= b[1]))
            { return false; }
        }
        return true;
    }
    public static double[]? FindFree(Model model, DrawingPlacement item, IReadOnlyList<DrawingPlacement> changed, int radius = 12, bool measured = false, Func<bool>? expired = null,
        Func<IReadOnlyList<DrawingPlacement>, bool>? accept = null)
    {
        radius = Math.Min(12, Math.Max(0, radius));
        double g = model.Page.GridSize;
        if (model.AllowUnknownGeometry && (!Finite(g) || g <= 0))
        { return null; }
        var search = new Model
        {
            Page = model.Page,
            AllowUnknownGeometry = model.AllowUnknownGeometry,
            DrawingArea = model.DrawingArea,
            Config = model.Config,
            Pins = model.Pins,
            PointRoutes = model.PointRoutes,
            PointRouteGaps = model.PointRouteGaps,
            Lines = model.Lines,
            // A suggested text move sets its position explicitly, so the candidate must clear R3 too.
            SetsTextAttribute = (row, attribute) => attribute == "position" && Text(item) && Same(item, row)
                || changed.Any(p => Same(p, row) && model.SetsTextAttribute?.Invoke(p, attribute) == true)
        };
        var offsets = Enumerable.Range(-radius, 2 * radius + 1).SelectMany(x => Enumerable.Range(-radius, 2 * radius + 1).Select(y => new[] { x, y }))
            .OrderBy(p => p[0] * p[0] + p[1] * p[1]).ThenBy(p => p[0]).ThenBy(p => p[1]);
        DrawingPlacement[] closure = changed.Where(p => Same(p, item) || Same(GroupOwner(p, changed), item)).ToArray();
        foreach (int[] offset in offsets)
        {
            if (expired?.Invoke() == true)
            { return null; }
            double[] point = item.Location.Select((n, i) => Math.Round(n / g, MidpointRounding.AwayFromZero) * g + offset[i] * g).ToArray();
            double[] delta = point.Zip(item.Location, (a, b) => a - b).ToArray();
            DrawingPlacement[] trial = changed.Except(closure).Concat(closure.Select(p => Shift(p, delta))).ToArray();
            if (!Check(search, trial, false, measured).Any(v => !v.Warning || v.Rule is "R7" or "R9") && accept?.Invoke(trial) != false)
            { return point; }
        }
        return null;
    }
    public static string Mode(Newtonsoft.Json.Linq.JToken? value) => value is null or { Type: Newtonsoft.Json.Linq.JTokenType.Null } ? "refuse"
        : value.Type == Newtonsoft.Json.Linq.JTokenType.String && (string?)value is "refuse" or "warn" ? ((string?)value)!
        : throw WriteArguments.Invalid(["/layout_guard: use refuse or warn."]);
    public static List<string> Enforce(Model model, IReadOnlyList<DrawingPlacement> changed, string at = "", bool measured = false, string mode = "refuse")
    {
        List<Violation> violations = Check(model, changed, measured: measured);
        if (model.AllowUnknownGeometry && (model.Page.PageSize is null || !Finite(model.Page.GridSize) || model.Page.GridSize <= 0))
        { OperationContext.Current.Warn("Layout page size or grid is unreadable; unavailable checks were skipped."); }
        if (mode == "warn")
        {
            foreach (Violation violation in violations.Where(v => v.Rule != "R1" || !OffPage(v.Geometry, model.Page.PageSize, model.AllowUnknownGeometry)))
            { violation.Warning = true; }
        }
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
        + (model.AllowUnknownGeometry && model.Page.PageSize is null ? "Boundary unverified: page size is unreadable. " : FrameArea(model) is null ? "Boundary fallback: Page.Size minus layout.marginMm." : "Boundary: plot-frame drawing area.")
        + (model.AllowUnknownGeometry && (!Finite(model.Page.GridSize) || model.Page.GridSize <= 0) ? " Grid unverified: snapping and R7 were skipped." : "");
    internal const int SummaryLines = 20;
    internal static readonly TimeSpan FreePositionSearchBudget = TimeSpan.FromSeconds(2);
    public static string Summary(IEnumerable<string?> messages)
    {
        string[] lines = [.. messages.Where(s => !string.IsNullOrEmpty(s)).SelectMany(s => s!.Split('\n'))
            .Distinct(StringComparer.Ordinal).OrderBy(s => s.StartsWith("Layout ", StringComparison.Ordinal) ? 0 : 1)];
        // Live 2026-11-02: an uncapped page-wide summary can push a write result past its token budget, which then drops
        // verification fields such as actualCores. Keep the first lines and say how many were left out.
        return lines.Length <= SummaryLines ? string.Join("\n", lines)
            : string.Join("\n", lines.Take(SummaryLines)) + "\n"
                + FormattableString.Invariant($"Layout: {lines.Length - SummaryLines} more warnings not shown; run eplan_page_review on the page for the full list.");
    }
    public static List<string> AfterWriting(Model before, Model? actual = null)
    {
        actual ??= before.ReadActual?.Invoke() ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Actual layout reader is unavailable; roll back the write.");
        actual.SetsTextAttribute = before.SetsTextAttribute;
        actual.AllowUnknownGeometry = before.AllowUnknownGeometry;
        var previous = new HashSet<string>(before.Page.Placements.Select(Newtonsoft.Json.JsonConvert.SerializeObject), StringComparer.Ordinal);
        DrawingPlacement[] changed = actual.Page.Placements.Where(p => !previous.Contains(Newtonsoft.Json.JsonConvert.SerializeObject(p))).ToArray();
        // A54: empty property texts report all-zero bounds and have no visible ink; they are not unmeasured objects.
        // ADR-0046: an outline EPLAN cannot measure is an unverified deviation, not a reason to roll back.
        DrawingPlacement[] unmeasured = [.. changed.Where(p => Positioned(p) && !DrawingCoordinates.ValidBounds(p.Bounds))];
        changed = [.. changed.Except(unmeasured)];
        actual.Lines = Networks(actual.Lines);
        foreach (Segment s in actual.Lines)
        { s.Changed = !before.Lines.Any(b => SegmentKey(b) == SegmentKey(s)); }
        // ADR-0046: after a write only an outline outside Page.Size (S2) still rolls back. Other measured findings stay in
        // the project as readback deviations, reported with their objects, measured geometry and a fix. Live 2026-11-02:
        // searching a free position for every committed deviation ran past the write limit, so only a refusal searches;
        // default texts keep their property_moves suggestion as before.
        // Live 2026-11-03: 167 objects and 646 findings kept the main thread busy for minutes on default-text searches;
        // suggestions before and after writing share one time budget per check.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        List<Violation> violations = Check(actual, changed, measured: true, searchViolations: false, searchBudget: FreePositionSearchBudget);
        if (timer.ElapsedMilliseconds > 2000)
        { OperationContext.Current.Warn(FormattableString.Invariant($"layout after write took {timer.ElapsedMilliseconds} ms for {changed.Length} changed objects and {violations.Count} findings")); }
        if (violations.Any(v => !v.Warning && v.Rule == "R1" && OffPage(v.Geometry, actual.Page.PageSize, actual.AllowUnknownGeometry)))
        {
            List<Violation> offPage = [.. Check(actual, changed, measured: true, searchBudget: FreePositionSearchBudget).Where(v => !v.Warning && v.Rule == "R1" && OffPage(v.Geometry, actual.Page.PageSize, actual.AllowUnknownGeometry))];
            BridgeErrorException error = WriteArguments.Invalid([.. offPage.Select(v => "S2: the new object lies outside the page. " + v.Message)]);
            error.Error.Metadata!["layoutViolations"] = Newtonsoft.Json.JsonConvert.SerializeObject(offPage);
            throw WriteArguments.NotStarted(error);
        }
        string Objects(IEnumerable<DrawingLocator> objects) => string.Join(", ", objects.Select(o => FormattableString.Invariant($"{o.Type}/{o.Oid}")));
        return [Report(actual, true), .. new[] { Notes(actual.Page.Placements) }.Where(s => s.Length != 0),
            .. unmeasured.Select(p => "Readback deviation (unverified): EPLAN reported no outline for " + Objects([p.Locator]) + "; check it with eplan_page_review."),
            .. violations.Select(v => v.Warning ? v.Message : "Readback deviation: " + v.Message + " Objects " + Objects(v.Objects) + ", measured ["
                + string.Join(", ", v.Geometry.Select(n => n.ToString("0.##", CultureInfo.InvariantCulture))) + "]; adjust with eplan_placement_update or check with eplan_page_review.")];
    }
    private static string PointKey(double[] p) => string.Join(",", p.Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
    private static string SegmentKey(Segment s) => string.Join(";", s.Points.Select(PointKey).OrderBy(p => p, StringComparer.Ordinal));
    /// <summary>Drops duplicate segments and names each by its network: segments that share an end point are one net.</summary>
    public static List<Segment> Networks(IEnumerable<Segment> lines)
    {
        List<Segment> unique = [.. lines.GroupBy(SegmentKey, StringComparer.Ordinal).Select(g => g.First())];
        var roots = unique.SelectMany(s => s.Points).Select(PointKey).Distinct(StringComparer.Ordinal).ToDictionary(p => p, p => p, StringComparer.Ordinal);
        string Root(string p)
        { while (roots[p] != p) { p = roots[p]; } return p; }
        foreach (Segment s in unique)
        { roots[Root(PointKey(s.Points[0]))] = Root(PointKey(s.Points[1])); }
        foreach (Segment s in unique)
        { s.Net = Root(PointKey(s.Points[0])); }
        return unique;
    }
    // S2 (ADR-0046): without a known page size an outline outside the drawing area cannot be cleared, so it still refuses.
    private static bool OffPage(double[] geometry, double[]? size, bool allowUnknown = false) => size is not { Length: 2 } ? !allowUnknown
        : geometry.Where((_, i) => i % 2 == 0).Any(x => x < 0 || x > size[0]) || geometry.Where((_, i) => i % 2 == 1).Any(y => y < 0 || y > size[1]);
    public static DrawingPlacement Shift(DrawingPlacement p, double[] delta) => new()
    {
        Locator = p.Locator,
        Parent = p.Parent,
        Kind = p.Kind,
        Properties = p.Properties,
        Location = p.Location.Length == 2 && p.Location.All(Finite) ? p.Location.Select((n, i) => n + delta[i]).ToArray() : p.Location,
        Bounds = ZeroBounds(p) ? p.Bounds : p.Bounds?.Select((n, i) => n + delta[i % 2]).ToArray()
    };
    public static Model From(PageScene scene) => new() { Page = scene.Page, Config = scene.Layout, Pins = new(scene.Pins), PointRoutes = scene.PointRoutes, PointRouteGaps = scene.PointRouteGaps, Lines = scene.Graphical.Select((s, i) => new Segment { Points = s, Net = null, Locator = new() { Oid = -i - 1 } }).ToList() };
    public static DrawingPlacement Proposed(string kind, double[] location, double[]? bounds, int index = 0) => new() { Kind = kind, Location = location, Bounds = bounds, Locator = new() { Oid = -index - 1 } };
    public static bool Overlaps(double[] a, double[] b, double clearance = 0) => a[0] < b[2] + clearance && a[2] + clearance > b[0] && a[1] < b[3] + clearance && a[3] + clearance > b[1];
    private static bool Text(DrawingPlacement p) => p.Kind is "Text" or "PathText" or "PropertyPlacement" or "HyperLink" or "DimensionItem" or "DimensionCircle";
    private static bool Graphic(DrawingPlacement p) => p.Kind is "PolyLine" or "Arc" or "Image" or "Spline" or "QRCode" or "PlaceHolder" or "MacroBox";
    internal static bool Container(DrawingPlacement p) => p.Properties.TryGetValue("layout_container", out string? value) && value == "true" || p.Kind is "LocationBox" or "PLC" or "PLCBox" or "Box";
    private static bool Symbol(DrawingPlacement p) => p.Kind != "DynamicConnectionLine" && !Container(p) && (p.Kind is "Function" or "SymbolReference" or "PotentialDefinition" or "ConnectionDefinitionPoint" or "Macro" || p.Properties.ContainsKey("layout_symbol"));
    // The project path comparison allocates; compare it last.
    private static bool OwnerId(DrawingLocator a, DrawingLocator b) => a.Oid == b.Oid && a.Type == b.Type && a.Page == b.Page && ProjectResolver.SamePath(a.Project, b.Project);
    private static bool SameOwner(DrawingPlacement p, DrawingPlacement q) => OwnerId(p.Parent ?? p.Locator, q.Parent ?? q.Locator);
    private static DrawingPlacement GroupOwner(DrawingPlacement item, IReadOnlyList<DrawingPlacement> changed)
    {
        var seen = new HashSet<DrawingPlacement> { item };
        while (item.Parent is { } parent && changed.FirstOrDefault(p => !Text(p) && OwnerId(p.Locator, parent)) is { } owner && seen.Add(owner))
        { item = owner; }
        return item;
    }
    /// <summary>The first non-text placement of each locator, as a scan of the rows in order would find it.</summary>
    private sealed class Owners(IReadOnlyList<DrawingPlacement> all)
    {
        private Dictionary<string, List<DrawingPlacement>>? _rows;
        public DrawingPlacement? Of(DrawingLocator parent)
        {
            _rows ??= all.Where(r => !Text(r)).GroupBy(r => Key(r.Locator), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            return _rows.TryGetValue(Key(parent), out List<DrawingPlacement>? rows) ? rows.FirstOrDefault(r => OwnerId(r.Locator, parent)) : null;
        }
        private static string Key(DrawingLocator l) => FormattableString.Invariant($"{l.Type}/{l.Oid}/{l.Page}");
    }
    internal static bool Related(DrawingPlacement p, DrawingPlacement q, IReadOnlyList<DrawingPlacement> all) => Related(p, q, new Owners(all));
    private static bool Related(DrawingPlacement p, DrawingPlacement q, Owners owners)
    {
        if (SameOwner(p, q))
        { return true; }
        DrawingPlacement Owner(DrawingPlacement row) => !Text(row) || row.Parent is null ? row : owners.Of(row.Parent) ?? row;
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
    internal static Func<DrawingPlacement, string, bool> TextMoveIntent(IReadOnlyList<DrawingPlacement> targets, bool setsAngle = false) => (row, attribute) =>
        (attribute == "position" || setsAngle && attribute == "angle") && Text(row) && targets.Any(p => p.Kind == row.Kind && OwnerId(p.Locator, row.Locator)
            && (row.Kind != "PropertyPlacement" || p.Parent is not null && row.Parent is not null && OwnerId(p.Parent, row.Parent)
                && PropertyMatches(p, row, "property_id") && PropertyMatches(p, row, "property_index")));
    private static bool PropertyMatches(DrawingPlacement p, DrawingPlacement q, string key) => p.Properties.TryGetValue(key, out string? value)
        && q.Properties.TryGetValue(key, out string? actual) && value == actual;
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
        if (p.Kind is "ConnectionDefinitionPoint" or "PotentialDefinition")
        {
            // A75 fifth run: rebinding changes existing type 59 points even when their owning route is unreadable.
            // Unknown ownership only warns on centre passage; known perpendicular routes and outline clips still refuse.
            List<double[][]>? routes = null;
            bool known = pointRoutes?.TryGetValue(owner, out routes) == true;
            double[] insertion = [p.Location[0], p.Location[1], p.Location[0], p.Location[1]];
            // A86 uses the insertion point on its owning wire; A75 rebound cable points can have an offset location.
            double[] centre = known && routes!.Any(route => Hits(route, insertion)) ? p.Location : DrawingCoordinates.ValidBounds(p.Bounds)
                ? [(p.Bounds![0] + p.Bounds[2]) / 2, (p.Bounds[1] + p.Bounds[3]) / 2] : p.Location;
            double[] box = [centre[0], centre[1], centre[0], centre[1]];
            if (!Hits(segment, box))
            { return false; }
            if (!known)
            { unverified = true; return true; }
            return routes!.Any(route => Hits(route, box)
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
        Dictionary<string, List<double[][]>>? pointRoutes = null, ISet<string>? endpointPins = null) => Crosses(s, p, pins, pointRoutes, out _, endpointPins);
    private static bool Crosses(double[][] s, DrawingPlacement p, Dictionary<string, SymbolConnectionPoint> pins,
        Dictionary<string, List<double[][]>>? pointRoutes, out bool unverified, ISet<string>? endpointPins = null)
    {
        unverified = false;
        // Live 2026-11-16: on a 3870-object page the pin scan below ran for every wire and box; most wires miss the outline.
        if (!Hits(s, p.Bounds!))
        { return false; }
        // A black-box connection point may belong to a child function, rather than to the box itself.
        bool endsAtBoxPin = (Container(p) || p.Kind == "BoxedDevice") && pins.Any(pin =>
            (endpointPins is null || endpointPins.Contains(pin.Key)) && Hits([pin.Value.Location, pin.Value.Location], p.Bounds!)
            && (s[0].SequenceEqual(pin.Value.Location) || s[s.Length - 1].SequenceEqual(pin.Value.Location)));
        return Hits(s, p.Bounds!) && !endsAtBoxPin && !ConnectedPoint(s, p, pins, pointRoutes, out unverified)
            && (p.Kind is "ConnectionDefinitionPoint" or "PotentialDefinition" || !pins.Any(pin => pin.Key.StartsWith(FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}/"), StringComparison.Ordinal)
            && Hits([pin.Value.Location, pin.Value.Location], p.Bounds!)
            && s.Any(end => end.SequenceEqual(pin.Value.Location) && s.Any(other => !other.SequenceEqual(end) && !Hits([other, other], p.Bounds!)
                && Faces(pin.Value, other)))));
    }
}
