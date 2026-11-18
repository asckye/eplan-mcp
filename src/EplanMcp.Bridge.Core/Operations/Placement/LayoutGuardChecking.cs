using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

public static partial class LayoutGuard
{
    /// <summary>
    /// One run of <see cref="Check"/>: the changed objects against the page, then the lines, then a free position for
    /// what was refused. The violations keep the order the checks find them in.
    /// </summary>
    private sealed class Checking(Model model, IReadOnlyList<DrawingPlacement> changed, bool measured)
    {
        private readonly List<Violation> _result = [];
        private readonly Dictionary<Violation, DrawingPlacement> _defaultTexts = [];
        private readonly HashSet<string> _unverifiedPoints = new(StringComparer.Ordinal);
        private double[] _area = [];
        private DrawingPlacement[] _all = [];
        private Owners _owners = new([]);
        private HashSet<DrawingPlacement> _changedRows = [];

        public List<Violation> Run(bool suggest, bool searchViolations, TimeSpan? searchBudget)
        {
            _area = Area(model);
            var changedIds = new HashSet<string>(changed.Select(c => Id(c.Locator)), StringComparer.Ordinal);
            _all = [.. model.Page.Placements.Where(p => !changedIds.Contains(Id(p.Locator))), .. changed];
            // Wire feeder 2026-11-11: 306 changed objects among macro pictures took 40 s, owner scans per pair; with one lookup a
            // 7181-object rack page checks 210 changed objects in 2.2 s instead of 14.5 s, with the same findings.
            _owners = new Owners(_all);
            _changedRows = new HashSet<DrawingPlacement>(changed);
            foreach (DrawingPlacement p in changed)
            { CheckPlacement(p); }
            foreach (Segment line in model.Lines)
            { CheckLine(line); }
            if (suggest)
            { Suggest(searchViolations, searchBudget); }
            return _result;
        }

        private void Add(string rule, DrawingPlacement p, DrawingLocator? other, string message, bool warningOnly = false, bool advisory = false)
        {
            bool warning = warningOnly || advisory || !model.Config.Strict && rule is "R6" or "R7" or "R9";
            _result.Add(new()
            {
                Rule = rule,
                Objects = other is null ? [p.Locator] : [p.Locator, other],
                Geometry = p.Bounds ?? p.Location,
                Warning = warning,
                Message = rule + ": " + (warning ? "Warning: " : "") + message + " " + (FrameArea(model) is null ? "Boundary fallback: Page.Size minus layout.marginMm; the plot frame reports no drawing area. " : "") + (warningOnly ? "Review the symbol's text layout." : "Choose another grid position or reroute.")
            });
        }

        private void TextConflict(DrawingPlacement p, DrawingPlacement q, string message)
        {
            DrawingPlacement[] texts = new[] { p, q }.Where(t => Text(t) && changed.Any(c => Same(c, t)
                || t.Parent is not null && !Text(c) && OwnerId(c.Locator, t.Parent))).ToArray();
            if (texts.Length == 0 || texts.Any(t => model.SetsTextAttribute?.Invoke(t, "position") == true))
            { Add("R3", p, q.Locator, message); return; }
            DrawingPlacement text = texts[0], other = ReferenceEquals(text, p) ? q : p;
            DrawingLocator owner = text.Parent ?? text.Locator;
            Add("R3", text, other.Locator, FormattableString.Invariant($"Text {Id(text.Locator)} owned by {owner.Type}/{owner.Oid} conflicts with {other.Kind} {Id(other.Locator)}; its position was not set by this call. Adjust the text with eplan_placement_update property_moves."), true);
            _defaultTexts.Add(_result[_result.Count - 1], text);
        }

        private void CheckPlacement(DrawingPlacement p)
        {
            if (!Positioned(p))
            { return; }
            double[] b = DrawingCoordinates.ValidBounds(p.Bounds) ? p.Bounds! : [p.Location[0], p.Location[1], p.Location[0], p.Location[1]];
            if (b[0] < _area[0] || b[1] < _area[1] || b[2] > _area[2] || b[3] > _area[3]
                || p.Location[0] < _area[0] || p.Location[1] < _area[1] || p.Location[0] > _area[2] || p.Location[1] > _area[3])
            {
                // A macro's declared outline is not what EPLAN draws (A154, live 2026-11-03: part overview macros fill the A3 page, as on the
                // sample's PLC overview pages). Inside a known page its objects are measured after the write, where only an off-page
                // outline rolls back (ADR-0046); without a known page size the outline still refuses.
                bool inPage = p.Kind == "Macro" && model.Page.PageSize is { Length: 2 } size && b[0] >= 0 && b[1] >= 0 && b[2] <= size[0] && b[3] <= size[1];
                Add("R1", p, null, inPage ? "The macro's declared outline leaves the drawing area; its objects are checked after the write." : "Keep the full outline inside the drawing area.", advisory: inPage);
            }
            if (p.Parent is null && (!model.AllowUnknownGeometry || Finite(model.Page.GridSize) && model.Page.GridSize > 0) && p.Location.Any(n => !OnGrid(n, model.Page.GridSize)))
            { Add("R7", p, null, "Placement points and bends must lie on the page grid; pins are exempt."); }
            // A text whose height or angle EPLAN does not report is not a violation: only a reported bad value refuses.
            bool badHeight = Height(p, out double height) && height < model.Config.MinimumTextHeightMm * PrintScale(model.Page);
            bool badAngle = Angle(p, out double angle) && Math.Abs(angle - Math.Round(angle / 90) * 90) > 1e-6;
            if (Text(p) && (badHeight || badAngle))
            {
                bool requested = badHeight && model.SetsTextAttribute?.Invoke(p, "height_mm") == true
                    || badAngle && model.SetsTextAttribute?.Invoke(p, "angle") == true;
                Add("R8", p, null, "Use the configured minimum text height and rotation 0, 90, 180 or 270 degrees.", !requested);
            }
            foreach (DrawingPlacement q in _all)
            { CheckPair(p, b, q); }
        }

        private void CheckPair(DrawingPlacement p, double[] b, DrawingPlacement q)
        {
            // Live 2026-11-16: 3870 objects took 11.8 s in page review; the outline test comes before the identity and
            // ownership comparisons, which build strings. Every rule below needs the outlines to meet.
            if (ReferenceEquals(p, q) || !Positioned(q) || !DrawingCoordinates.ValidBounds(q.Bounds)
                || !Overlaps(b, q.Bounds!, model.Config.SymbolClearanceMm) && !(p.Bounds is null && p.Location.SequenceEqual(q.Location)) || Same(p, q))
            { return; }
            bool overlap = Overlaps(b, q.Bounds!);
            if (Related(p, q, _owners))
            {
                if (SameOwner(p, q) && (Text(p) || Text(q)) && overlap)
                { Add("R3", p, q.Locator, "Text overlaps within the same owner; symbol-default positions may need adjustment.", true); }
                return;
            }
            if ((Symbol(p) || Graphic(p) || Container(p)) && (Symbol(q) || Graphic(q) || Container(q)) && (Overlaps(b, q.Bounds!, model.Config.SymbolClearanceMm) || p.Bounds is null && p.Location.SequenceEqual(q.Location)))
            { Add("R2", p, q.Locator, "A symbol outline conflicts with another placement. A moved outline conflicts with another placement."); }
            if ((Text(p) && (Text(q) || Symbol(q) || Graphic(q) || Container(q) || q.Kind == "Line") || Text(q) && (Symbol(p) || Graphic(p) || Container(p) || p.Kind == "Line")) && overlap)
            { TextConflict(p, q, "Text bounds conflict with another text, symbol or line."); }
            if (q.Properties.TryGetValue("cross_reference", out string? cross) && cross == "true" && overlap)
            { Add("R9", p, q.Locator, "Keep clear of existing cross-reference text."); }
        }

        private void CheckLine(Segment line)
        {
            double[] box = Box(line.Points);
            var row = new DrawingPlacement { Locator = line.Locator, Kind = "Line", Location = line.Points[0], Bounds = box };
            if (line.Changed && (box[0] < _area[0] || box[1] < _area[1] || box[2] > _area[2] || box[3] > _area[3]))
            { Add("R1", row, null, "Keep the complete line inside the drawing area."); }
            foreach (DrawingPlacement p in _all.Where(p => line.Changed || _changedRows.Contains(p)))
            { CheckAgainstLine(line, row, p); }
            if (!line.Changed)
            { return; }
            if ((!model.AllowUnknownGeometry || Finite(model.Page.GridSize) && model.Page.GridSize > 0) && line.Points.Any(p => !model.Pins.Values.Any(pin => pin.Location.SequenceEqual(p)) && p.Any(n => !OnGrid(n, model.Page.GridSize))))
            { Add("R7", row, null, "Line bends must lie on the page grid; pin endpoints are exempt."); }
            CheckSpacing(line, row, box);
        }

        private void CheckAgainstLine(Segment line, DrawingPlacement row, DrawingPlacement p)
        {
            if (!Positioned(p) || !DrawingCoordinates.ValidBounds(p.Bounds))
            { return; }
            if (Symbol(p))
            {
                if (Crosses(line.Points, p, model.Pins, model.PointRoutes, out bool unverified))
                { Add("R4", p, line.Locator, "The path crosses the outline of a symbol; a moved outline conflicts with an autoconnect line."); }
                if (unverified)
                {
                    string owner = FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}");
                    if (_unverifiedPoints.Add(owner))
                    {
                        string reason = model.PointRouteGaps.TryGetValue(owner, out string? gap) ? gap : "route evidence unavailable";
                        _result.Add(new()
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
            if (Text(p) && Hits(line.Points, p.Bounds!) && !Related(p, row, _owners))
            { TextConflict(p, row, "Text bounds conflict with a line."); }
        }

        private void CheckSpacing(Segment line, DrawingPlacement row, double[] box)
        {
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

        private void Suggest(bool searchViolations, TimeSpan? searchBudget)
        {
            var searchTimer = System.Diagnostics.Stopwatch.StartNew();
            Func<TimeSpan> searched = model.SearchElapsed ?? (() => searchTimer.Elapsed);
            bool searchExpired = false;
            bool Expired() => searchExpired |= searched() >= searchBudget!.Value;
            string NoPosition() => searchExpired ? " Free-position search stopped at the time limit; check the page with eplan_page_review."
                : " No free position found within the bounded search.";
            foreach (DrawingPlacement p in changed.Where(p => searchViolations && _result.Any(v => !v.Warning && v.Objects[0] == p.Locator)))
            {
                DrawingPlacement owner = GroupOwner(p, changed);
                double[]? free = Expired() ? null : FindFree(model, owner, changed, measured: measured, expired: Expired);
                foreach (Violation v in _result.Where(v => !v.Warning && v.Objects[0] == p.Locator))
                { v.NearestFreePosition = free; v.Message += free is null ? NoPosition() : FormattableString.Invariant($" Suggested position: [{free[0]}, {free[1]}] mm."); }
            }
            foreach (IGrouping<DrawingPlacement, KeyValuePair<Violation, DrawingPlacement>> group in _defaultTexts.GroupBy(pair => pair.Value))
            {
                DrawingPlacement text = group.Key;
                double[]? free = Expired() ? null : FindFree(model, text, [.. changed.Where(p => !Same(p, text)), text], measured: measured, expired: Expired);
                foreach (var pair in group)
                { pair.Key.NearestFreePosition = free; pair.Key.Message += free is null ? NoPosition() : FormattableString.Invariant($" Suggested position for property_moves: [{free[0]}, {free[1]}] mm."); }
            }
        }
    }
}
