using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A54: read every location inside the SafetyPoint; autoconnect is checked after commit, before CloseUndo.</summary>
public static class PlacementMoveExecution
{
    public static JObject Execute(IPlacementMover mover, ITransactionScopeFactory transactions, JObject input, string fingerprint, string tool = "eplan_placement_update")
    {
        ConnectionCreatePlanner.Scene scene;
        try
        { scene = mover.Read((string?)input["project"] ?? "", (string?)input["page"] ?? ""); }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        JObject plan = PlacementMovePlanner.Plan(input.ToString(Formatting.None), scene, fingerprint);
        plan["dryRun"] = false;
        if ((bool)plan["noOp"]!)
        { return plan; }
        Action move = mover.Prepare(scene, (JArray)plan["moves"]!);
        ConnectionCreatePlanner.Scene Read() => mover.Read(scene.Project.Path, scene.Page.Page);
        var expected = BridgeJson.FromObject(scene).ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
        JToken[] rows = plan["moves"]!.ToArray();
        LayoutGuard.Model beforeLayout = LayoutGuard.From(scene);
        beforeLayout.SetsTextAttribute = LayoutGuard.TextMoveIntent(scene.Page.Placements.Where(p => (input.ContainsKey("property_moves") || p.Parent is null)
            && rows.Any(r => DrawingLocators.Matches(r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!, p.Locator))).ToArray(), input.ContainsKey("property_moves"));
        foreach (DrawingPlacement placement in expected.Page.Placements)
        {
            JToken? row = rows.SingleOrDefault(r => DrawingLocators.Matches(r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!, placement.Locator));
            if (row is null)
            { continue; }
            double[] delta = row["after"]!.Values<double>().Zip(placement.Location, (a, b) => a - b).ToArray();
            placement.Location = row["after"]!.Values<double>().ToArray();
            placement.Bounds = row["bounds"]!.Type == JTokenType.Null ? null : row["bounds"]!.Values<double>().ToArray();
            if (row["angle"] is { } angle)
            { placement.Properties["angle_deg"] = ((double)angle).ToString("R", System.Globalization.CultureInfo.InvariantCulture); placement.Properties["docked"] = "false"; if (placement.Properties.ContainsKey("angle")) { placement.Properties["angle"] = placement.Properties["angle_deg"]; } }
            if (row["style"] is JObject style)
            { foreach (var field in PropertyPlacementMoves.StyleValues(style)) { placement.Properties[field.Key] = field.Value; } }
            if (placement.Parent is null)
            {
                string prefix = FormattableString.Invariant($"{placement.Locator.Type}/{placement.Locator.Oid}/");
                foreach (var pin in expected.Pins.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)))
                { pin.Value.Location = pin.Value.Location.Zip(delta, (a, b) => a + b).ToArray(); }
            }
        }
        string Segments(IEnumerable<double[][]> segments) => string.Join(";", segments.Select(s => string.Join("|", s.Select(p => new JArray(p).ToString(Formatting.None)).OrderBy(p => p, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
        foreach (JToken change in plan["graphicalChanges"]!)
        {
            if (change["before"] is JArray b)
            { expected.Graphical.RemoveAll(s => Segments([s]) == Segments([b.ToObject<double[][]>()!])); }
            if (change["after"] is JArray a)
            { expected.Graphical.Add(a.ToObject<double[][]>()!); }
        }
        DrawingPlacement[] movedCables = scene.Page.Placements.Where(p => p.Kind == "Cable" && rows.Any(r => DrawingLocators.Matches(
            r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!, p.Locator) && !JToken.DeepEquals(r["before"], r["after"]))).ToArray();
        void Verify(bool committed)
        {
            ConnectionCreatePlanner.Scene actual = Read();
            // A75: points can be added before old crossings/core membership are reconciled by generation.
            DrawingPlacement[] cables = movedCables.Concat(actual.Page.Placements.Where(p => p.Kind == "Cable"
                && movedCables.Any(c => c.Locator.Type == p.Locator.Type && c.Locator.Oid == p.Locator.Oid))).ToArray();
            // The points' own property texts go with them, also when only one side still has the point (live 2026-11-02:
            // an unnamed new point and its text were added, the old point's text was gone).
            // Points are judged per side, so another cable's point that changes owner still differs.
            DrawingPlacement[] before = [.. expected.Page.Placements.Where(p => cables.Any(c => PlacementMovePlanner.IsCablePoint(p, c)))];
            DrawingPlacement[] after = [.. actual.Page.Placements.Where(p => cables.Any(c => PlacementMovePlanner.IsCablePoint(p, c))
                || PlacementMovePlanner.IsNewCablePoint(p, scene.Page.Placements, cables))];
            bool PointText(DrawingPlacement p) => p.Parent is { } parent && before.Concat(after).Any(q => q.Locator.Type == parent.Type && q.Locator.Oid == parent.Oid);
            expected.Page.Placements.RemoveAll(p => before.Contains(p) || PointText(p));
            actual.Page.Placements.RemoveAll(p => after.Contains(p) || PointText(p));
            // Described before the size-only bounds normalization below, so a mismatch reports the measured bounds.
            JObject[] observed = actual.Page.Placements.Select(PlacementMoveMismatch.Describe).ToArray();
            BridgeErrorException Mismatch(string check, string detail, object want, object found) => PlacementMoveMismatch.Create(check, detail, scene, expected, actual, observed, want, found);
            LayoutGuard.Model actualLayout = LayoutGuard.From(actual);
            if (!committed)
            { actualLayout.Lines = expected.Graphical.Select(s => new LayoutGuard.Segment { Points = s }).ToList(); }
            plan["warning"] = LayoutGuard.Summary(new[] { (string?)plan["warning"] }.Concat(LayoutGuard.AfterWriting(beforeLayout, actualLayout)));
            var measuredBounds = new List<(JToken Row, double[]? Bounds)>();
            if (actual.Page.Placements.Count != expected.Page.Placements.Count)
            { throw Mismatch("count", "placement count", expected.Page.Placements.Count, actual.Page.Placements.Count); }
            for (int i = 0; i < expected.Page.Placements.Count; i++)
            {
                DrawingPlacement want = expected.Page.Placements[i], found = actual.Page.Placements[i];
                if (want.Bounds is null)
                { want.Bounds = found.Bounds; }
                JToken? row = rows.SingleOrDefault(r => DrawingLocators.Matches(r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!, want.Locator));
                if (row is not null)
                {
                    measuredBounds.Add((row, found.Bounds));
                    found.Locator.Digest = want.Locator.Digest;
                    // A74: freshly created property text can have stale outline offsets. The approved bounds
                    // already include any 90/270-degree size swap; compare size while keeping the measured outline.
                    if (want.Kind == "PropertyPlacement" && found.Bounds is { Length: 4 } bounds && want.Bounds is { Length: 4 } expectedBounds
                        && Math.Abs((bounds[2] - bounds[0]) - (expectedBounds[2] - expectedBounds[0])) <= 0.01
                        && Math.Abs((bounds[3] - bounds[1]) - (expectedBounds[3] - expectedBounds[1])) <= 0.01)
                    { found.Bounds = expectedBounds; }
                    if (row["style"] is JObject style)
                    {
                        if ((bool?)style["visible"] == false && found.Properties.ContainsKey("visible"))
                        { found.Properties["visible"] = "false"; }
                        if (style["alignment"] is not null || style["height_mm"] is not null)
                        { want.Bounds = found.Bounds; }
                    }
                    if (found.Parent is not null && want.Parent is not null)
                    { found.Parent.Digest = want.Parent.Digest; }
                }
                bool computed = IsFollowingBlockValue(want, found);
                if (computed)
                {
                    // A72: EPLAN recomputes the read-only block value on the other members of a moved interruption group.
                    foreach (string key in new[] { "text", "display" })
                    { if (want.Properties.TryGetValue(key, out string? value) && found.Properties.ContainsKey(key)) { found.Properties[key] = value; } }
                }
                if (computed)
                { found.Locator.Digest = want.Locator.Digest; if (found.Parent is not null && want.Parent is not null) { found.Parent.Digest = want.Parent.Digest; } }
            }
            if (!actual.Complete || actual.PageType != scene.PageType)
            { throw Mismatch("closure", "page inventory", new { scene.Complete, scene.PageType }, new { actual.Complete, actual.PageType }); }
            for (int i = 0; i < expected.Page.Placements.Count; i++)
            {
                JToken want = BridgeJson.FromObject(expected.Page.Placements[i]), found = BridgeJson.FromObject(actual.Page.Placements[i]);
                if (!JToken.DeepEquals(want, found))
                {
                    throw Mismatch((string?)want["kind"] is "PropertyPlacement" or "Text" ? "text" : "closure",
                    FormattableString.Invariant($"placement {i} {(string?)want["kind"]} {want["locator"]!["type"]}/{want["locator"]!["oid"]}"), want, found);
                }
            }
            if (!JToken.DeepEquals(BridgeJson.FromObject(expected.Page), BridgeJson.FromObject(actual.Page)))
            { throw Mismatch("closure", "page geometry", expected.Page, actual.Page); }
            if (!JToken.DeepEquals(BridgeJson.FromObject(expected.Pins), BridgeJson.FromObject(actual.Pins)))
            { throw Mismatch("pins", "pins", expected.Pins, actual.Pins); }
            if (!JToken.DeepEquals(BridgeJson.FromObject(scene.Logical), BridgeJson.FromObject(actual.Logical)))
            { throw Mismatch("connections", "logical connections", scene.Logical, actual.Logical); }
            if (committed && Segments(expected.Graphical) != Segments(actual.Graphical))
            { throw Mismatch("connections", "autoconnect lines", Segments(expected.Graphical).Split(';'), Segments(actual.Graphical).Split(';')); }
            if (committed)
            {
                foreach (var measured in measuredBounds)
                { measured.Row["bounds"] = measured.Bounds is null ? null : new JArray(measured.Bounds); }
            }
        }
        Committed<bool> result = TransactionRunner.Run(transactions, tool, () =>
        {
            move();
            Verify(false);
            return true;
        }, _ => true,
            _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => { Verify(true); return true; }),
            () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => PlacementMovePlanner.Plan(input.ToString(Formatting.None), Read(), fingerprint)));
        plan["execution"] = ExecutionState.Committed;
        plan["warning"] = LayoutGuard.Summary([(string?)plan["warning"], result.Warning]);
        return plan;

        bool IsFollowingBlockValue(DrawingPlacement want, DrawingPlacement found)
        {
            if (want.Kind != "PropertyPlacement" || found.Kind != want.Kind || want.Parent is null || found.Parent is null
                || !want.Properties.TryGetValue("displayed_property_id", out string? id) || id != "20201"
                || !found.Properties.TryGetValue("displayed_property_id", out string? actualId) || actualId != id)
            { return false; }
            DrawingPlacement? owner = scene.Page.Placements.FirstOrDefault(p => p.Parent is null && p.Kind == "InterruptionPoint"
                && p.Locator.Type == want.Parent.Type && p.Locator.Oid == want.Parent.Oid);
            return owner?.Properties.TryGetValue("name", out string? name) == true && scene.Page.Placements.Any(p => p.Parent is null && p.Kind == "InterruptionPoint"
                && p.Properties.TryGetValue("name", out string? movedName) && movedName == name
                && rows.Any(r => DrawingLocators.Matches(r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!, p.Locator) && !JToken.DeepEquals(r["before"], r["after"])));
        }
    }
}
