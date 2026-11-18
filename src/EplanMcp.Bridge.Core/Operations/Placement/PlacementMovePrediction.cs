using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

public static partial class PlacementMovePlanner
{
    /// <summary>
    /// One move prediction, in the order <see cref="Run"/> lists: the request, the targets and what follows them, the
    /// layout after the move, then the autoconnect lines that change. The order of the checks is the order of the refusals.
    /// </summary>
    private sealed class Prediction(JObject input, PageScene scene)
    {
        private readonly List<(DrawingPlacement Row, double[] Requested)> _targets = [];
        private readonly Dictionary<DrawingPlacement, double[]> _offsets = [];
        private readonly JArray _rows = new();
        private string _guard = "";
        private string _fingerprint = "";
        private PageDrawingGeometry _page = new();
        private DrawingPlacement[] _cables = [];
        private bool _noOp;
        private bool _movesPins;
        private LayoutGuard.Model _layout = new();
        private DrawingPlacement[] _proposed = [];
        private List<string> _layoutWarnings = [];
        private Dictionary<string, SymbolConnectionPoint> _pins = [];
        private Dictionary<string, double[][]> _before = new(StringComparer.Ordinal);
        private Dictionary<string, double[][]> _after = new(StringComparer.Ordinal);
        private string[] _changed = [];

        public JObject Run(string? expectedFingerprint)
        {
            ReadRequest(expectedFingerprint);
            ReadTargets();
            CheckPage();
            ShiftTargets();
            CheckLayout();
            _pins = scene.Pins.ToDictionary(p => p.Key, p => new SymbolConnectionPoint { Direction = p.Value.Direction, Location = p.Value.Location.ToArray() }, StringComparer.Ordinal);
            if (_movesPins)
            {
                MovePins();
                CheckChangedLines();
            }
            else
            { CheckDrawnLines(); }
            return Result(Affected());
        }

        private void ReadRequest(string? expectedFingerprint)
        {
            ConnectionCreatePlanner.Members(input, "", "project page moves", "dry_run snap_to_grid response_format layout_guard");
            _guard = LayoutGuard.Mode(input["layout_guard"]);
            ConnectionCreatePlanner.FiniteNumbers(input, "");
            Require(input["project"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["project"]), "/project", "Specify an open project.");
            Require(input["page"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["page"]), "/page", "Specify an exact Circuit page.");
            foreach (string name in new[] { "dry_run", "snap_to_grid" })
            { Require(input[name] is null || input[name]!.Type == JTokenType.Boolean, "/" + name, "Supply a boolean."); }
            Require(input["response_format"] is null || input["response_format"]!.Type == JTokenType.String && (string?)input["response_format"] is "concise" or "detailed", "/response_format", "Use concise or detailed.");
            Require(input["moves"] is JArray { Count: >= 1 and <= 50 }, "/moves", "Supply 1..50 moves.");
            _fingerprint = Fingerprint.Of([input.ToString(Formatting.None), BridgeJson.FromObject(scene).ToString(Formatting.None)]);
            if (expectedFingerprint is not null && expectedFingerprint != _fingerprint)
            { throw Stale("Page layout, children or connections changed; create a new dry-run plan."); }
        }

        private void ReadTargets()
        {
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
                Require(!_targets.Any(t => Same(t.Row.Locator, row.Locator)), at + "/locator", "Remove duplicate targets.");
                JArray coordinates = delta ? move["delta_mm"] as JArray ?? new JArray() : new JArray(move["x_mm"]!, move["y_mm"]!);
                Require(coordinates.Count == 2 && coordinates.All(n => n.Type is JTokenType.Integer or JTokenType.Float && (double)n >= (delta ? -10000 : 0) && (double)n <= 10000), at, "Supply two finite mm coordinates; deltas may be negative.");
                if (row.Location.Length != 2 || row.Location.Any(n => double.IsNaN(n) || double.IsInfinity(n)))
                { throw Missing("Read the full placement location."); }
                _targets.Add((row, coordinates.Select((n, j) => (double)n + (delta ? row.Location[j] : 0)).ToArray()));
            }
        }

        private void CheckPage()
        {
            _page = scene.Page;
            if (!scene.Complete || scene.PageType != "Circuit" || _page.Project != scene.Project.Path || (string?)input["page"] != _page.Page
                || ((string?)input["project"] != scene.Project.Name && !ProjectResolver.SamePath((string)input["project"]!, _page.Project))
                || !DrawingCoordinates.Contains(_page.PageSize, [[0, 0, 0, 0]]) || _page.Placements.Count > 10000 || scene.Pins.Count > 10000)
            { throw Missing("Read a complete Circuit page with reliable Page.Size and full placement outlines."); }
        }

        private void ShiftTargets()
        {
            _cables = _page.Placements.Where(p => p.Kind == "Cable" && _targets.Any(t => Same(p.Locator, t.Row.Locator)
                || p.Parent is not null && Same(p.Parent, t.Row.Locator))).ToArray();
            DrawingCoordinates.Snap(0, _page.GridSize);
            foreach (var target in _targets)
            {
                double[] actual = target.Requested.SequenceEqual(target.Row.Location) ? target.Requested : target.Requested.Select(n => DrawingCoordinates.Snap(n, _page.GridSize, (bool?)input["snap_to_grid"] ?? true)).ToArray();
                double[] offset = actual.Zip(target.Row.Location, (a, b) => a - b).ToArray();
                // A75: EPLAN reconciles this cable's definition points only when connections are generated.
                DrawingPlacement[] closure = [target.Row, .. _page.Placements.Where(p => p.Parent is not null && Same(p.Parent, target.Row.Locator))];
                foreach (DrawingPlacement child in closure.Where(p => !_cables.Any(c => IsCablePoint(p, c))))
                {
                    if (!child.Properties.TryGetValue("grouped", out string? grouped) || grouped != "false" || child.Kind is not ("Function" or "SymbolReference" or "PotentialDefinition" or "InterruptionPoint" or "Cable" or "ConnectionDefinitionPoint" or "Text" or "Line" or "Rectangle" or "PropertyPlacement"))
                    { throw Missing("Grouped objects, macro groups and unverified placement kinds cannot be moved."); }
                    _offsets.Add(child, offset);
                    // A54: all-zero outlines have no visible ink; unknown positions do not constrain the move.
                    DrawingPlacement shifted = LayoutGuard.Shift(child, offset);
                    double[] bounds = shifted.Bounds ?? (shifted.Location.Length == 2 ? [shifted.Location[0], shifted.Location[1], shifted.Location[0], shifted.Location[1]] : []);
                    Require(!LayoutGuard.Positioned(child) || DrawingCoordinates.Contains(_page.PageSize, [bounds]) && bounds.All(n => n <= 10000), "/moves", string.Format(System.Globalization.CultureInfo.InvariantCulture, "Keep every following outline inside Page.Size and 0..10000 mm ({0} at {1}: outline {2} moved to {3}).",
                        child.Kind, string.Join(",", child.Location), string.Join(",", child.Bounds ?? []), string.Join(",", bounds)));
                    _rows.Add(new JObject { ["locator"] = BridgeJson.FromObject(child.Locator), ["parent"] = child.Parent is null ? null : BridgeJson.FromObject(child.Parent), ["before"] = new JArray(child.Location), ["requested"] = new JArray(child.Location.Select((n, j) => n + target.Requested[j] - target.Row.Location[j])), ["after"] = new JArray(shifted.Location), ["snapped"] = !actual.SequenceEqual(target.Requested), ["bounds"] = shifted.Bounds is null ? null : new JArray(shifted.Bounds) });
                }
            }
        }

        private void CheckLayout()
        {
            _noOp = _offsets.Values.All(d => d.All(n => n == 0));
            _movesPins = _offsets.Any(move => move.Value.Any(n => n != 0)
                && scene.Pins.Keys.Any(pin => Owner(pin) == Id(move.Key.Locator)));
            _layout = LayoutGuard.From(scene);
            _layout.SetsTextAttribute = LayoutGuard.TextMoveIntent(_targets.Select(t => t.Row).ToArray());
            if (_movesPins)
            {
                _layout.Lines.Clear();
            }
            _proposed = _offsets.Where(p => p.Value.Any(n => n != 0)).Select(p => LayoutGuard.Shift(p.Key, p.Value)).ToArray();
            _layoutWarnings = LayoutGuard.Enforce(_layout, _proposed, "/moves: ", mode: _guard);
            DrawingPlacement[] predicted = _page.Placements.Select(Predicted).ToArray();
            Require(_guard == "warn" || LayoutGuard.ClearOfOthers(predicted, [.. _offsets.Keys.Where(p => LayoutGuard.Positioned(p) && _offsets[p].Any(n => n != 0)).Select(Predicted)]),
                "/moves", "A moved outline conflicts with another placement.");
        }

        private static string Owner(string pin) => string.Join("/", pin.Split('/').Take(2));
        private static string Id(DrawingLocator locator) => FormattableString.Invariant($"{locator.Type}/{locator.Oid}");

        private double[] Box(DrawingPlacement p)
        {
            double[] d = _offsets.TryGetValue(p, out double[]? o) ? o : [0, 0];
            return p.Bounds?.All(n => n == 0) != false
                ? [p.Location[0] + d[0], p.Location[1] + d[1], p.Location[0] + d[0], p.Location[1] + d[1]]
                : p.Bounds!.Select((n, j) => n + d[j % 2]).ToArray();
        }

        private DrawingPlacement Predicted(DrawingPlacement p) => _offsets.TryGetValue(p, out double[]? d) ? LayoutGuard.Shift(p, d) : p;

        private static string Segment(double[][] s) => string.Join("|", s.Select(p => string.Join(",", p.Select(n => n.ToString("R", CultureInfo.InvariantCulture)))).OrderBy(p => p, StringComparer.Ordinal));

        private void MovePins()
        {
            if (_pins.Values.Any(p => p.Location.Length != 2 || p.Location.Any(n => double.IsNaN(n) || double.IsInfinity(n)) || Opposite(p.Direction) == "")
                || scene.Logical.Any(c => new[] { c.StartDevice, c.EndDevice }.Any(k => k is not null && !_pins.ContainsKey(k)))
                || _pins.Any(p => !_page.Placements.Any(r => r.Parent is null && Id(r.Locator) == Owner(p.Key))))
            {
                throw Missing("Read finite cardinal pin geometry with known owners.");
            }
            _before = Autoconnect(_pins);
            if (!_before.Values.Select(Segment).OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(scene.Graphical.Select(Segment).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)))
            {
                throw Missing("Autoconnect geometry is not fully predictable from this page's pins.");
            }
            foreach (var pin in _pins)
            {
                double[]? offset = _targets.Where(t => Id(t.Row.Locator) == Owner(pin.Key)).Select(t => _offsets[t.Row]).SingleOrDefault();
                if (offset is not null)
                {
                    pin.Value.Location = pin.Value.Location.Zip(offset, (a, b) => a + b).ToArray();
                }
            }
            _after = Autoconnect(_pins);
            _layout.Pins = _pins;
            _layout.Lines = _after.Select((p, i) => new LayoutGuard.Segment { Points = p.Value, Changed = !_before.TryGetValue(p.Key, out double[][]? old) || Segment(old) != Segment(p.Value), Net = p.Key, Locator = new() { Oid = -i - 1000 } }).ToList();
            _layoutWarnings.AddRange(LayoutGuard.Enforce(_layout, _proposed, "/moves: ", mode: _guard));
            Require(!_pins.GroupBy(p => string.Join(",", p.Value.Location.Select(n => n.ToString("R", CultureInfo.InvariantCulture))), StringComparer.Ordinal).Any(g => g.Select(p => Owner(p.Key)).Distinct(StringComparer.Ordinal).Count() > 1), "/moves", "Pins from different placements must not coincide.");
        }

        private void CheckChangedLines()
        {
            // A64/A75: these annotate wires; EPLAN resolves cable membership after generation, then eplan_page_review checks it.
            foreach (DrawingPlacement moved in _offsets.Keys.Where(p => p.Kind is not ("Cable" or "ConnectionDefinitionPoint") && LayoutGuard.Positioned(p) && _offsets[p].Any(n => n != 0)))
            {
                double[] b = Box(moved);
                Require(_guard == "warn" || !_after.Any(l => !l.Key.Split('|').Any(k => Owner(k) == Id(moved.Parent ?? moved.Locator)) && Math.Min(l.Value[0][0], l.Value[1][0]) <= b[2] && Math.Max(l.Value[0][0], l.Value[1][0]) >= b[0] && Math.Min(l.Value[0][1], l.Value[1][1]) <= b[3] && Math.Max(l.Value[0][1], l.Value[1][1]) >= b[1]), "/moves", "A moved outline conflicts with an autoconnect line.");
            }
            _changed = _before.Keys.Union(_after.Keys, StringComparer.Ordinal).Where(k => !_before.ContainsKey(k) || !_after.ContainsKey(k) || Segment(_before[k]) != Segment(_after[k])).ToArray();
            foreach (string key in _changed.Where(_after.ContainsKey))
            {
                double[][] line = _after[key];
                Require(!_after.Any(l => l.Key != key && ConnectionCreatePlanner.Intersects(line, l.Value) && !l.Value.Any(a => line.Any(b => a.SequenceEqual(b)))), "/moves", "Changed autoconnect lines have an ambiguous crossing.");
                Require(_guard == "warn" || !_page.Placements.Any(p => LayoutGuard.Positioned(p) && !key.Split('|').Any(k => Owner(k) == Id(p.Parent ?? p.Locator)) && Box(p) is var b
                    && Math.Min(line[0][0], line[1][0]) <= b[2] && Math.Max(line[0][0], line[1][0]) >= b[0] && Math.Min(line[0][1], line[1][1]) <= b[3] && Math.Max(line[0][1], line[1][1]) >= b[1]), "/moves", "A changed autoconnect line conflicts with another placement.");
            }
        }

        private void CheckDrawnLines()
        {
            // No moved placement owns a pin, so autoconnect stays as drawn; an outline moved onto one of its lines is still refused.
            foreach (DrawingPlacement moved in _offsets.Keys.Where(p => p.Kind is not ("Cable" or "ConnectionDefinitionPoint") && LayoutGuard.Positioned(p) && _offsets[p].Any(n => n != 0)))
            {
                double[] b = Box(moved);
                Require(_guard == "warn" || !scene.Graphical.Any(l => l.Length >= 2 && Math.Min(l[0][0], l[1][0]) <= b[2] && Math.Max(l[0][0], l[1][0]) >= b[0]
                    && Math.Min(l[0][1], l[1][1]) <= b[3] && Math.Max(l[0][1], l[1][1]) >= b[1]), "/moves", "A moved outline conflicts with an autoconnect line.");
            }
        }

        private HashSet<string> Affected()
        {
            Require(_pins.Values.All(p => p.Location.All(n => n is >= 0 and <= 10000) && DrawingCoordinates.Contains(_page.PageSize, [[p.Location[0], p.Location[1], p.Location[0], p.Location[1]]])), "/moves", "Keep all following pins inside Page.Size.");
            var affected = new HashSet<string>(_changed.SelectMany(k => k.Split('|')), StringComparer.Ordinal);
            bool expanded;
            do
            {
                int count = affected.Count;
                foreach (var pin in _pins.Where(p => affected.Any(a => Owner(a) == Owner(p.Key)) && _page.Placements.Any(r => r.Parent is null && Id(r.Locator) == Owner(p.Key) && r.Kind != "Function")).ToArray())
                { affected.Add(pin.Key); }
                foreach (string edge in _before.Keys.Concat(_after.Keys).Where(k => k.Split('|').Any(affected.Contains)).ToArray())
                { affected.UnionWith(edge.Split('|')); }
                expanded = affected.Count != count;
            } while (expanded);
            return affected;
        }

        private JObject Result(HashSet<string> affected)
        {
            var result = new JObject
            {
                ["project"] = scene.Project.Name,
                ["page"] = _page.Page,
                ["execution"] = ExecutionState.NotStarted,
                ["dryRun"] = true,
                ["noOp"] = _noOp,
                ["gridSize"] = _page.GridSize,
                ["boundarySource"] = "Page.Size",
                ["moves"] = _rows,
                ["fingerprint"] = _fingerprint,
                ["graphicalChanges"] = new JArray(_changed.Select(k => new JObject { ["pins"] = new JArray(k.Split('|')), ["before"] = _before.TryGetValue(k, out double[][]? b) ? JArray.FromObject(b) : null, ["after"] = _after.TryGetValue(k, out double[][]? a) ? JArray.FromObject(a) : null })),
                ["connectionChanges"] = JArray.FromObject(_noOp ? [] : scene.Logical.Where(c => affected.Contains(c.StartDevice ?? "") || affected.Contains(c.EndDevice ?? "")).Select(c => new[] { c.StartDevice, c.EndDevice })),
                ["generation"] = "Logical endpoints are preserved; use eplan_connection_generate after moving to regenerate connections."
            };
            if (_offsets.Any(p => p.Key.Kind == "Cable" && p.Value.Any(n => n != 0)))
            { _layoutWarnings.Add("Moving a cable drops its core assignment (A73): EPLAN recreates its definition points unnamed and eplan_connection_generate does not make the crossed connections cable cores again. Check the cores with eplan_page_review; undo (Ctrl+Z) restores them."); }
            result["warning"] = _layoutWarnings.Count == 0 ? null : LayoutGuard.Summary(_layoutWarnings);
            string encoded = result.ToString(Formatting.None);
            Require((encoded.Count(c => c <= 127) + 3) / 4 + encoded.Count(c => c > 127) <= ((string?)input["response_format"] == "detailed" ? 20000 : 5000) - 256, "/response_format", "Use detailed or fewer moves; complete impact cannot be truncated.");
            return result;
        }
    }
}
