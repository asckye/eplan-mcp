using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Detached, read-only page rules; electrical crossings require target-network evidence (A52/A64/A65).</summary>
public static class PageReviewRules
{
    private static readonly string[] _coreFields = ["label", "colour"];
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
        foreach (DrawingPlacement p in placements)
        {
            if (Enabled("off_grid") && scene.Page.GridSize > 0 && p.Location.Any(v => Math.Abs(v / scene.Page.GridSize - Math.Round(v / scene.Page.GridSize)) > 1e-8))
            { Add("off_grid", "info", p.Location, [p.Locator], "Placement is off the page grid; library pin offsets are exempt.", "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(p.Locator), ["x"] = Math.Round(p.Location[0] / scene.Page.GridSize) * scene.Page.GridSize, ["y"] = Math.Round(p.Location[1] / scene.Page.GridSize) * scene.Page.GridSize }); }
            if (!Enabled("overlaps") || p.Bounds is not { Length: 4 } a || a[0] == a[2] || a[1] == a[3])
            { continue; }
            foreach (DrawingPlacement q in placements.SkipWhile(q => !ReferenceEquals(q, p)).Skip(1))
            {
                if (q.Bounds is { Length: 4 } b && a[0] < b[2] && a[2] > b[0] && a[1] < b[3] && a[3] > b[1] && (p.Properties.ContainsKey("name") || p.Kind == "Text") && (q.Properties.ContainsKey("name") || q.Kind == "Text"))
                { Add("overlap", "warning", p.Location, [p.Locator, q.Locator], "Symbol or text bounds overlap.", "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(q.Locator) }); }
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
