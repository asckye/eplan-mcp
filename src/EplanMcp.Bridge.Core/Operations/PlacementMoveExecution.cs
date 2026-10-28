using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A54: read every location inside the SafetyPoint; autoconnect is checked after commit, before CloseUndo.</summary>
public static class PlacementMoveExecution
{
    public static JObject Execute(IPlacementMover mover, ITransactionScopeFactory transactions, JObject input, string fingerprint)
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
        foreach (DrawingPlacement placement in expected.Page.Placements)
        {
            JToken? row = rows.SingleOrDefault(r => DrawingLocators.Matches(r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!, placement.Locator));
            if (row is null)
            { continue; }
            double[] delta = row["after"]!.Values<double>().Zip(placement.Location, (a, b) => a - b).ToArray();
            placement.Location = row["after"]!.Values<double>().ToArray();
            placement.Bounds = row["bounds"]!.Values<double>().ToArray();
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
        void Verify(bool committed)
        {
            ConnectionCreatePlanner.Scene actual = Read();
            if (actual.Page.Placements.Count != expected.Page.Placements.Count)
            { throw Mismatch("placement count"); }
            for (int i = 0; i < expected.Page.Placements.Count; i++)
            {
                DrawingPlacement want = expected.Page.Placements[i], found = actual.Page.Placements[i];
                if (rows.Any(r => DrawingLocators.Matches(r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!, want.Locator)))
                { found.Locator.Digest = want.Locator.Digest; if (found.Parent is not null && want.Parent is not null) { found.Parent.Digest = want.Parent.Digest; } }
            }
            if (!actual.Complete || actual.PageType != scene.PageType)
            { throw Mismatch("page inventory"); }
            for (int i = 0; i < expected.Page.Placements.Count; i++)
            {
                JToken want = BridgeJson.FromObject(expected.Page.Placements[i]), found = BridgeJson.FromObject(actual.Page.Placements[i]);
                if (!JToken.DeepEquals(want, found))
                { throw Mismatch("placement " + want.ToString(Formatting.None) + " read back as " + found.ToString(Formatting.None)); }
            }
            if (!JToken.DeepEquals(BridgeJson.FromObject(expected.Page), BridgeJson.FromObject(actual.Page)))
            { throw Mismatch("page geometry"); }
            if (!JToken.DeepEquals(BridgeJson.FromObject(expected.Pins), BridgeJson.FromObject(actual.Pins)))
            { throw Mismatch("pins"); }
            if (!JToken.DeepEquals(BridgeJson.FromObject(scene.Logical), BridgeJson.FromObject(actual.Logical)))
            { throw Mismatch("logical connections"); }
            if (committed && Segments(expected.Graphical) != Segments(actual.Graphical))
            { throw Mismatch("autoconnect lines " + Segments(actual.Graphical) + " instead of " + Segments(expected.Graphical)); }
        }
        Committed<bool> result = TransactionRunner.Run(transactions, "eplan_placement_update", () => { move(); Verify(false); return true; }, _ => true,
            _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => { Verify(true); return true; }),
            () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => PlacementMovePlanner.Plan(input.ToString(Formatting.None), Read(), fingerprint)));
        plan["execution"] = ExecutionState.Committed;
        plan["warning"] = result.Warning;
        return plan;
    }
    private static BridgeErrorException Mismatch(string what) => new(ErrorReason.EplanOperationFailed, "Placement closure, following text, pins or connections differ from the approved move (" + what + ").");
}
