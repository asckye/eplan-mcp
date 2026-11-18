using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

public static partial class ConnectionCreatePlanner
{
    private sealed partial class Prediction
    {
        private void SplitExisting()
        {
            _existingSegments = _generate ? scene.Graphical.GroupBy(SegmentKey, StringComparer.Ordinal).Select(g => g.First()).ToArray() : [];
            foreach (var node in _nodes.Values)
            {
                double[][][] existing = _existingSegments.Where(s => On(node.Point, s)).ToArray();
                if (existing.Length == 0)
                { continue; }
                Require(existing.Length == 1 && node.Kind == "t_node" && !existing[0].Any(p => p.SequenceEqual(node.Point))
                    && (existing[0][0][0] == existing[0][1][0] || existing[0][0][1] == existing[0][1][1])
                    && existing[0].All(p => node.Directions.Contains(Direction(node.Point, p), StringComparer.Ordinal)), "/items",
                    "Only a T-node strictly inside one existing autoconnect segment with both aligned directions may split it.");
                Require(!_splits.Any(s => ReferenceEquals(s.Segment, existing[0])), "/items", "At most one node may split each existing autoconnect segment per call.");
                _splits.Add((existing[0], node.Point));
            }
        }

        private void ResolveNodes()
        {
            List<SymbolSearchItem> special = _variants ??= reader.Read(scene.Project.Path, new() { Library = "SPECIAL" });
            foreach (var node in _nodes.Values)
            { ResolveNode(node, special); }
        }

        private SymbolSearchItem[] Matches((double[] Point, string[] Directions, string Kind) node, List<SymbolSearchItem> special)
        {
            return special.Where(s => s.Library == "SPECIAL" && (node.Kind switch
            {
                "corner" => s.Name == "CO" && s.Type == "Routing",
                "autoconnect_break" => s.Name == "ACBP" && s.Type == "ConnectionBreakpoint",
                "t_node" => s.Type == (s.Name switch { "TLRO" => "TNodeUp", "TLRU" => "TNodeDown", "TOUL" => "TNodeLeft", "TOUR" => "TNodeRight", _ => "" }),
                "interruption_point" => s.Type == "InterruptionPoint" && s.Name == (_interruptionSymbols.TryGetValue(Key(node.Point), out string? named) ? named : "BP"),
                _ => s.Name == "PCP" && s.Type == "PotentialTerminal",
            }) && (node.Kind == "interruption_point" ? s.ConnectionPoints.Count > 0 : s.ConnectionPoints.Count == node.Directions.Length) && s.ConnectionPoints.All(p => p.Location.Length == 2
                && p.Location.All(v => !double.IsNaN(v) && !double.IsInfinity(v) && (node.Kind == "interruption_point" || Math.Abs(v) <= 2 * _grid))
                && (node.Kind is "potential_point" or "interruption_point" || _requestedVariants.ContainsKey(Key(node.Point)) || _directionalVariants.ContainsKey(Key(node.Point)) || p.Location.All(v => v == 0)))
                && (node.Kind == "interruption_point" ? s.ConnectionPoints.Any(p => p.Direction == node.Directions[0])
                    : s.ConnectionPoints.Select(p => p.Direction).OrderBy(d => d, StringComparer.Ordinal).SequenceEqual(node.Directions.OrderBy(d => d, StringComparer.Ordinal))))
                // A83: prefer BP's tail (index 1), then the lowest variant number, regardless of library enumeration order.
                .OrderByDescending(s => node.Kind == "interruption_point" ? s.ConnectionPoints.FindLastIndex(p => p.Direction == node.Directions[0]) : -1)
                .ThenBy(s => s.Variant).ToArray();
        }

        private SymbolSearchItem[] Narrow((double[] Point, string[] Directions, string Kind) node, SymbolSearchItem[] matches)
        {
            if (_requestedVariants.TryGetValue(Key(node.Point), out var requestedVariant))
            {
                Require(matches.Any(s => s.Variant == requestedVariant.Number), requestedVariant.Pointer,
                    "Available variants for the requested cardinal pins: " + string.Join(", ", matches.Select(s => s.Variant.ToString(CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal)) + ".");
                matches = matches.Where(s => s.Variant == requestedVariant.Number).ToArray();
            }
            else if (_directionalVariants.TryGetValue(Key(node.Point), out var directional))
            {
                // A project without the directional variant keeps the old choice: the one variant with every pin at the node.
                Require(!directional.Explicit || matches.Any(s => s.Variant == directional.Number), directional.Pointer,
                    "The project's SPECIAL library has no directional variant " + directional.Number.ToString(CultureInfo.InvariantCulture) + " for these pins.");
                matches = matches.Any(s => s.Variant == directional.Number) ? matches.Where(s => s.Variant == directional.Number).ToArray()
                    : matches.Where(s => s.ConnectionPoints.All(p => p.Location.All(v => v == 0))).ToArray();
            }
            // A115: current runs left to right and top to bottom, so a BP the wire leaves rightwards or downwards is where it
            // comes in: arrow into the page, name outside (E, H). The other ends keep the outgoing tail variant.
            else if (node.Kind == "interruption_point" && matches.FirstOrDefault()?.Name == "BP"
                && (node.Directions[0] switch { "Right" => 4, "Down" => 7, _ => -1 }) is var incoming && matches.Any(s => s.Variant == incoming))
            { matches = matches.Where(s => s.Variant == incoming).ToArray(); }
            // A52/S40: PCP has one offset pin and mirrored variants sharing its direction.
            if (node.Kind is "potential_point" or "interruption_point" && matches.Length > 0)
            { matches = matches.Where(s => s.Variant == matches[0].Variant).ToArray(); }
            return matches;
        }

        private void ResolveNode((double[] Point, string[] Directions, string Kind) node, List<SymbolSearchItem> special)
        {
            SymbolSearchItem[] candidates = Matches(node, special);
            SymbolSearchItem[] matches = Narrow(node, candidates);
            SymbolSearchItem symbol = matches.Length == 1 ? matches[0] : throw Missing("Select a verified SPECIAL variant with the requested cardinal pins using eplan_symbol_search.");
            if (node.Kind == "t_node" && !_requestedVariants.ContainsKey(Key(node.Point)) && _directionalVariants.TryGetValue(Key(node.Point), out var directional) && symbol.Variant == directional.Number)
            { _chosenTNodes.Add((node, _resolved.Count, symbol, [.. candidates.Where(s => s.Name == symbol.Name && s.Variant != symbol.Variant)])); }
            _resolved.Add(Placed(node, symbol));
        }

        /// <summary>The symbol at its insertion point, so that its first pin (an interruption point's: the pin of its direction) lies on the node.</summary>
        private JObject Placed((double[] Point, string[] Directions, string Kind) node, SymbolSearchItem symbol)
        {
            double[] offset = (node.Kind == "interruption_point" ? symbol.ConnectionPoints.Last(p => p.Direction == node.Directions[0]) : symbol.ConnectionPoints[0]).Location;
            double[] position = [node.Point[0] - offset[0], node.Point[1] - offset[1]];
            foreach (SymbolConnectionPoint pin in symbol.ConnectionPoints)
            { _nodePins[Key(node.Point) + "/" + pin.Direction] = [position[0] + pin.Location[0], position[1] + pin.Location[1]]; }
            string key = FormattableString.Invariant($"{symbol.Name}/{symbol.Variant}");
            double[] bounds = scene.SymbolBounds.TryGetValue(key, out double[]? found) && DrawingCoordinates.ValidBounds(found) ? found : [0, 0, 0, 0];
            double[] outline = [bounds[0] + position[0], bounds[1] + position[1], bounds[2] + position[0], bounds[3] + position[1]];
            Require(position.All(v => v is >= 0 and <= 10000) && DrawingCoordinates.Contains(scene.Page.PageSize, [outline, [position[0], position[1], position[0], position[1]]]), "/items", "Keep the full symbol outline inside Page.Size and insertion coordinates in 0..10000 mm.");
            // Functional discovery metadata does not widen the existing routing-symbol contract.
            var placed = new JObject { ["position"] = new JArray(position), ["symbol"] = BridgeJson.FromObject(new { symbol.Library, symbol.Name, symbol.Variant, symbol.Type, symbol.Kind, symbol.ConnectionPoints }), ["bounds"] = new JArray(outline) };
            placed["boundsKnown"] = DrawingCoordinates.ValidBounds(found);
            if (_interruptions.TryGetValue(Key(node.Point), out JObject? interruption))
            { placed["interruption"] = interruption; }
            return placed;
        }

        private void BuildSegments()
        {
            _segments = _paths.SelectMany(p => p.Zip(p.Skip(1), (a, b) => new[] { a, b })).ToList();
            _segments.AddRange(_splits.SelectMany(s => s.Segment.Select(p => new[] { p, s.Point })));
            _endpointPins.UnionWith(scene.Pins.Where(p => _splits.Any(s => s.Segment.Any(end => end.SequenceEqual(p.Value.Location)))).Select(p => p.Key));
            _ports = _generate ? scene.Pins.Values.Select(p => (Point: p.Location, p.Direction)).Concat(_nodes.Values.SelectMany(n => n.Directions.Select(d => (n.Point, Direction: d)))).ToArray() : [];
            foreach (var node in _nodes.Values.Where(_ => _generate))
            {
                foreach (string direction in node.Directions)
                {
                    double[][] candidates = _ports.Where(p => p.Direction == Opposite(direction) && !p.Point.SequenceEqual(node.Point) && Direction(node.Point, p.Point) == direction
                        && (p.Point[0] == node.Point[0] || p.Point[1] == node.Point[1])).Select(p => p.Point).OrderBy(p => Distance(p, node.Point)).ToArray();
                    if (candidates.Length > 0 && !_segments.Any(s => On(node.Point, s) && s.Any(p => !p.SequenceEqual(node.Point) && Direction(node.Point, p) == direction)))
                    {
                        _segments.Add([node.Point, candidates[0]]);
                        _endpointPins.UnionWith(scene.Pins.Where(p => p.Value.Location.SequenceEqual(candidates[0])).Select(p => p.Key));
                    }
                }
            }
            _segments = _segments.SelectMany(s =>
            {
                double[][] points = s.Concat(_nodes.Values.Select(n => n.Point).Where(p => On(p, s))).GroupBy(Key, StringComparer.Ordinal).Select(g => g.First()).OrderBy(p => Distance(s[0], p)).ToArray();
                return points.Zip(points.Skip(1), (a, b) => new[] { a, b });
            }).ToList();
        }

        private void CheckSegments()
        {
            // Customer rehearsal: a new segment may end at an existing corner or T-node centre whose other drawn segment
            // leaves at a right angle there; meeting at that free port is a connection, not a conflict.
            _nodeCentres = new HashSet<string>(scene.Pins.GroupBy(p => Owner(p.Key), StringComparer.Ordinal)
                .Where(g => g.Count() > 1 && g.Select(p => Key(p.Value.Location)).Distinct(StringComparer.Ordinal).Count() == 1).Select(g => Key(g.First().Value.Location)), StringComparer.Ordinal);
            for (int i = 0; i < _segments.Count; i++)
            { CheckSegment(i); }
        }

        private bool MeetsAtNode(double[][] a, double[][] b) => (a[0][0] == a[1][0]) != (b[0][0] == b[1][0])
            && a.Any(p => b.Any(q => q.SequenceEqual(p)) && _nodeCentres.Contains(Key(p)));

        private void CheckSegment(int i)
        {
            double[][] s = _segments[i];
            // Replacement halves inherit the old segment's contacts at its original endpoints.
            bool replacement = _splits.Any(split => s.Any(p => p.SequenceEqual(split.Point)) && s.Any(p => split.Segment.Any(end => end.SequenceEqual(p))));
            double[][]? conflict = replacement ? null : _existingSegments.FirstOrDefault(e => Intersects(s, e) && !MeetsAtNode(s, e) && !Crossing(s, e) && !_splits.Any(split => ReferenceEquals(split.Segment, e)
                && s.Any(p => p.SequenceEqual(split.Point)) && (s[0][0] == s[1][0]) != (e[0][0] == e[1][0])));
            Require(conflict is null, "/items", conflict is null ? "" : "The path " + FormatPoint(s[0]) + " -> " + FormatPoint(s[1]) + " conflicts with the existing autoconnect line "
                + FormatPoint(conflict[0]) + " -> " + FormatPoint(conflict[1]) + "; plan an explicit replacement separately.");
            if (_guard != "warn" && scene.Page.Placements.Where(Obstacle).FirstOrDefault(p => LayoutGuard.Crosses(s, p, scene.Pins, scene.PointRoutes, _endpointPins)) is { } crossed)
            {
                Require(false, "/items", FormattableString.Invariant(
                    $"The path {FormatPoint(s[0])} -> {FormatPoint(s[1])} crosses the outline of {crossed.Kind} {crossed.Locator.Type}/{crossed.Locator.Oid} [{string.Join(",", crossed.Bounds!.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))}]. R4: reroute around the outline."));
            }
            Require(!scene.Pins.Values.Any(p => On(p.Location, s) && !s.Any(e => e.SequenceEqual(p.Location))), "/items", "A different pin intercepts this path; select explicit endpoints.");
            foreach (var node in _nodes.Values.Where(n => On(n.Point, s)))
            { Require(s.Where(p => !p.SequenceEqual(node.Point)).All(p => node.Directions.Contains(Direction(node.Point, p), StringComparer.Ordinal)), "/items", "A node's directions do not support this path."); }
            for (int j = 0; j < i; j++)
            {
                if (!Intersects(s, _segments[j]) || Crossing(s, _segments[j]))
                { continue; }
                double[][] ends = s.Concat(_segments[j]).Concat(_nodes.Values.Select(n => n.Point)).Where(p => On(p, s) && On(p, _segments[j])).GroupBy(Key, StringComparer.Ordinal).Select(g => g.First()).ToArray();
                Require(ends.Length == 1 && (_nodes.TryGetValue(Key(ends[0]), out var junction) && junction.Kind == "t_node" || s.Any(p => p.SequenceEqual(ends[0])) && _segments[j].Any(p => p.SequenceEqual(ends[0]))), "/items", "Reject ambiguous crossings or overlapping segments; declare a T-node explicitly.");
            }
        }

        private void GroupConnected()
        {
            var vertices = _ports.Select(p => p.Point).Concat(_segments.Concat(_existingSegments).SelectMany(s => s)).GroupBy(Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _groups = vertices.Keys.ToDictionary(k => k, k => new HashSet<string>(StringComparer.Ordinal) { k }, StringComparer.Ordinal);
            // A55 acceptance trial: existing drawn edges continue through node centres, never through device bodies.
            foreach (double[][] segment in _segments.Concat(_existingSegments))
            {
                var group = new HashSet<string>(segment.SelectMany(p => _groups[Key(p)]), StringComparer.Ordinal);
                foreach (string key in group)
                { _groups[key] = group; }
            }
            // Logical evidence also retains nets whose drawn route spans several existing nodes.
            foreach (ConnectionRecord before in scene.Logical.Where(_ => _generate))
            {
                if (before.StartDevice is null || before.EndDevice is null || !scene.Pins.TryGetValue(before.StartDevice, out SymbolConnectionPoint? a)
                    || !scene.Pins.TryGetValue(before.EndDevice, out SymbolConnectionPoint? b))
                { continue; }
                var group = new HashSet<string>(_groups[Key(a.Location)].Concat(_groups[Key(b.Location)]), StringComparer.Ordinal);
                foreach (string key in group)
                { _groups[key] = group; }
            }
            // Acceptance trial: an existing corner or T-node's pins are not logical ends (EPLAN connects the devices through it),
            // and a point no new segment touches predicts nothing. A64: a variant's offset pin still belongs to the same node.
            _nodeOwners = NodeOwners(scene.Pins.Select(p => (p.Key, p.Value.Location)), scene.Page.Placements.Where(p => p.Kind == "SymbolReference").Select(p => FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}")));
            foreach (string owner in _nodeOwners.Where(_ => _generate))
            {
                var group = new HashSet<string>(scene.Pins.Where(p => Owner(p.Key) == owner).SelectMany(p => _groups[Key(p.Value.Location)]), StringComparer.Ordinal);
                foreach (string key in group)
                { _groups[key] = group; }
            }
        }

        /// <summary>
        /// A148: EPLAN wires a T-node as a chain, source to target 1 to target 2 (its points are listed [target 1, source,
        /// target 2]); with no device on target 1's side it generates no connection at all, also not between the other two.
        /// The default makes the branch target 1 (EES). Where that side reaches no device yet and target 2's side does, the
        /// variant with the same source and the targets exchanged keeps the chain closed. A given variant is never changed.
        /// </summary>
        private void KeepChainsClosed()
        {
            foreach (var chosen in _chosenTNodes)
            {
                List<SymbolConnectionPoint> points = chosen.Symbol.ConnectionPoints;
                if (ReachesDevice(chosen.Node.Point, points[0].Direction) || !ReachesDevice(chosen.Node.Point, points[2].Direction))
                { continue; }
                SymbolSearchItem? exchanged = chosen.Others.Where(s => s.ConnectionPoints[0].Direction == points[2].Direction && s.ConnectionPoints[1].Direction == points[1].Direction)
                    .OrderBy(s => s.Variant).FirstOrDefault();
                if (exchanged is not null)
                { _resolved[chosen.Index] = Placed(chosen.Node, exchanged); }
            }
        }

        /// <summary>Whether the lines leaving the node in this direction lead to a device pin, a potential point or an interruption point, not counting the node itself.</summary>
        private bool ReachesDevice(double[] node, string direction)
        {
            double[][][] lines = [.. _segments, .. _existingSegments.Where(e => !_splits.Any(s => ReferenceEquals(s.Segment, e)))];
            double[]? next = _segments.Where(s => s.Any(p => p.SequenceEqual(node))).SelectMany(s => s).FirstOrDefault(p => !p.SequenceEqual(node) && Direction(node, p) == direction);
            var seen = new HashSet<string>(StringComparer.Ordinal) { Key(node) };
            var open = new Queue<double[]>();
            if (next is not null && seen.Add(Key(next)))
            { open.Enqueue(next); }
            while (open.Count > 0)
            {
                double[] at = open.Dequeue();
                string[] owners = [.. scene.Pins.Where(p => p.Value.Location.SequenceEqual(at)).Select(p => Owner(p.Key))];
                if (owners.Any(o => !_nodeOwners.Contains(o)) || _nodes.TryGetValue(Key(at), out var added) && added.Kind is "potential_point" or "interruption_point")
                { return true; }
                // An existing corner or T-node passes the line on through all its pins (A64: one may be offset).
                IEnumerable<double[]> onwards = lines.Where(s => s.Any(p => p.SequenceEqual(at))).SelectMany(s => s)
                    .Concat(scene.Pins.Where(p => owners.Contains(Owner(p.Key), StringComparer.Ordinal)).Select(p => p.Value.Location));
                foreach (double[] point in onwards.Where(p => seen.Add(Key(p))))
                { open.Enqueue(point); }
            }
            return false;
        }

        private void PredictLogical()
        {
            var touched = new HashSet<string>(_segments.SelectMany(s => s).Select(Key), StringComparer.Ordinal);
            foreach (HashSet<string> connected in _groups.Values.Distinct().Where(g => g.Overlaps(touched)))
            {
                string[] ends = scene.Pins.Where(p => connected.Contains(Key(p.Value.Location)) && !_nodeOwners.Contains(Owner(p.Key))).Select(p => p.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                bool potential = _nodes.Values.Any(n => n.Kind == "potential_point" && connected.Contains(Key(n.Point)));
                if (_splits.Count > 0 && !potential && ends.Length > 1)
                {
                    var preserved = new HashSet<string>(StringComparer.Ordinal) { ends[0] };
                    for (int i = 0; i < ends.Length; i++)
                    {
                        foreach (ConnectionRecord before in scene.Logical.Where(c => c.StartDevice is not null && c.EndDevice is not null
                            && ends.Contains(c.StartDevice, StringComparer.Ordinal) && ends.Contains(c.EndDevice, StringComparer.Ordinal)
                            && (preserved.Contains(c.StartDevice) || preserved.Contains(c.EndDevice))))
                        { preserved.Add(before.StartDevice!); preserved.Add(before.EndDevice!); }
                    }
                    if (preserved.SetEquals(ends))
                    { continue; }
                }
                if (ends.Length > 1 || potential && ends.Length > 0)
                { _logical.Add(new JObject { ["pins"] = new JArray(ends), ["potentialStartIsNull"] = potential }); }
            }
        }

        private List<string> Layout()
        {
            LayoutGuard.Model layout = LayoutGuard.From(scene);
            string Net(double[][] s) => _groups[Key(s[0])].OrderBy(k => k, StringComparer.Ordinal).First();
            layout.Lines = _generate ? layout.Lines.Where(l => !_splits.Any(s => SegmentKey(s.Segment) == SegmentKey(l.Points))).ToList() : [];
            foreach (LayoutGuard.Segment line in layout.Lines)
            { line.Net = Net(line.Points); }
            // Logical grouping above uses node points; drawn lines (layout, readback comparison) end at the variant's real pins.
            double[] Drawn(double[] end, double[] other) => _nodePins.TryGetValue(Key(end) + "/" + Direction(end, other), out double[]? pin) ? pin : end;
            _drawn = _segments.Select(s => new[] { Drawn(s[0], s[1]), Drawn(s[1], s[0]) }).ToList();
            layout.Lines.AddRange(_segments.Select((s, i) => new LayoutGuard.Segment { Points = _drawn[i], Changed = true, Net = Net(s), Locator = new() { Oid = -i - 1000 } }));
            DrawingPlacement[] proposed = _resolved.Select((p, i) => LayoutGuard.Proposed("SymbolReference", p["position"]!.ToObject<double[]>()!, p["bounds"]!.ToObject<double[]>()!, i)).ToArray();
            for (int i = 0; i < proposed.Length; i++)
            { foreach (SymbolConnectionPoint pin in _resolved[i]["symbol"]!["connectionPoints"]!.ToObject<List<SymbolConnectionPoint>>()!) { layout.Pins.Add(FormattableString.Invariant($"0/{proposed[i].Locator.Oid}/{layout.Pins.Count}"), new() { Direction = pin.Direction, Location = pin.Location.Zip(proposed[i].Location, (a, b) => a + b).ToArray() }); } }
            layout.PointRoutes = new(scene.PointRoutes);
            foreach (JToken definition in _definitions)
            {
                double[] point = definition["position"]!.ToObject<double[]>()!;
                DrawingPlacement placed = LayoutGuard.Proposed((string)definition["type"]!, point, [point[0], point[1], point[0], point[1]], proposed.Length);
                layout.PointRoutes[$"0/{placed.Locator.Oid}"] = layout.Lines.Where(l => On(point, l.Points)).Select(l => l.Points).ToList();
                proposed = [.. proposed, placed];
            }
            return LayoutGuard.Enforce(layout, proposed, "/items: ", mode: _guard);
        }

        private JObject Result(List<string> layoutWarnings)
        {
            var result = new JObject
            {
                ["project"] = scene.Project.Name,
                ["page"] = scene.Page.Page,
                ["execution"] = ExecutionState.NotStarted,
                ["dryRun"] = true,
                ["coordinates"] = _requested,
                ["symbols"] = _resolved,
                ["graphicalAutoconnect"] = JArray.FromObject(_drawn),
                ["logicalAfterGenerate"] = _logical,
                ["gridSize"] = _grid,
                ["pageSize"] = new JArray(scene.Page.PageSize!),
                ["coverage"] = "complete_page_predicted_endpoints",
                ["logicalDuringPlacement"] = 0,
                ["generation"] = _logical.Count == 0 && _splits.Count == 0
                    ? "No logical endpoints: place and read back nodes without connection generation; 1 undo step."
                    : "One call commits placement then generation: 2 undo steps. Generation failure undoes placement automatically.",
                ["undoSteps"] = _logical.Count == 0 && _splits.Count == 0 ? 1 : 2
            };
            result["warning"] = layoutWarnings.Count == 0 ? null : LayoutGuard.Summary(layoutWarnings);
            if (_splits.Count > 0)
            { result["splitSegments"] = new JArray(_splits.Select(s => new JObject { ["segment"] = JArray.FromObject(s.Segment), ["position"] = new JArray(s.Point) })); }
            if (_generate && _interruptions.Values.Any(i => i["counterpart_pages"] is not null))
            { InterruptionPointPlanning.Plan(result, scene, reader as IConnectionCreator ?? throw Missing("Read explicit counterpart scope before planning interruption points.")); }
            if (_definitions.Count > 0)
            { result["definitionPoints"] = _definitions; result["undoSteps"] = 1; if (_definitions.Any(d => (string?)d["type"] == "ConnectionDefinitionPoint") || _logical.Count > 0) { result["generation"] = "Placement, definition points and connection generation share one undo step."; }; }
            if (!_generate)
            { result["generation"] = "Deferred: place and read back objects; call eplan_connection_generate after all placements. 1 undo step."; result["undoSteps"] = 1; result["coverage"] = "placement_only"; }
            return result;
        }

        private JObject Seal(JObject result, string? expectedFingerprint)
        {
            string cableFingerprint = CableDefinitionPlanner.Add(input, scene, result, Point);
            string fingerprint = Fingerprint.Of([input.ToString(Formatting.None), .. _definitions.Count > 0 ? new[] { _definitions.ToString(Formatting.None) } : Array.Empty<string>(), .. (cableFingerprint == "" ? Array.Empty<string>() : new[] { cableFingerprint }), BridgeJson.FromObject(new { scene.Project, PageProject = scene.Page.Project, scene.Page.Page, scene.Page.GridSize, scene.Page.PageSize, scene.PageType, scene.Complete, scene.Layout }).ToString(Formatting.None), _resolved.ToString(Formatting.None),
                .. scene.Page.Placements.Select(p => "placement:" + BridgeJson.FromObject(p).ToString(Formatting.None)), .. scene.Pins.Select(p => "pin:" + p.Key + BridgeJson.FromObject(p.Value).ToString(Formatting.None)),
                .. scene.PointRoutes.Select(p => "point-route:" + p.Key + BridgeJson.FromObject(p.Value).ToString(Formatting.None)),
                .. scene.PointRouteGaps.Select(p => "point-route-gap:" + p.Key + BridgeJson.FromObject(p.Value).ToString(Formatting.None)),
                .. scene.Graphical.Select(p => "graphical:" + BridgeJson.FromObject(p).ToString(Formatting.None)), .. scene.Logical.Select(p => "logical:" + BridgeJson.FromObject(p).ToString(Formatting.None)), .. scene.SymbolBounds.Select(p => "outline:" + p.Key + BridgeJson.FromObject(p.Value).ToString(Formatting.None)), .. result["interruptionScope"] is { } evidence ? new[] { evidence.ToString(Formatting.None) } : []]);
            result["fingerprint"] = fingerprint;
            return expectedFingerprint is null || expectedFingerprint == fingerprint ? result : throw new BridgeErrorException(ErrorReason.PlanStale, "Page, pins or library geometry changed; create a new dry-run plan.");
        }
    }
}
