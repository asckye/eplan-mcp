using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>unintended_junctions: a read connection joins two networks the expected connections keep apart.</summary>
internal static class UnintendedJunctionsCheck
{
    public static void Run(ReviewContext c)
    {
        if (!c.Enabled("unintended_junctions") || c.Targets["connections"] is not JArray targets)
        { return; }
        var networks = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (JToken target in targets)
        {
            string a = c.End(target["from"]), b = c.End(target["to"]);
            var joined = new HashSet<string>((networks.TryGetValue(a, out var first) ? first : [a]).Concat(networks.TryGetValue(b, out var second) ? second : [b]), StringComparer.Ordinal);
            foreach (string key in joined)
            { networks[key] = joined; }
        }
        foreach (JToken connection in c.Actual["connections"]!)
        {
            string a = c.End(connection["from"]), b = c.End(connection["to"]);
            if (networks.TryGetValue(a, out var group) && networks.ContainsKey(b) && !group.Contains(b))
            { c.Add("unintended_junction", "error", [], connection["locator"] is JObject l ? [l.ToObject<DrawingLocator>()!] : [], "A logical connection joins separate target networks.", "eplan_placement_update", new() { ["page"] = c.Scene.Page.Page }); }
        }
    }
}

/// <summary>interruption_pairing: EPLAN pairs adjacent same-name interruption points (A65); the pair differs from the expected counterpart.</summary>
internal static class InterruptionPairingCheck
{
    public static void Run(ReviewContext c)
    {
        if (!c.Enabled("interruption_pairing"))
        { return; }
        foreach (var group in c.Actual["interruptions"]!.GroupBy(p => (string?)p["name"], StringComparer.Ordinal))
        {
            JToken[] ordered = group.OrderBy(p => (int)p["pageOrder"]!).ThenBy(p => (double)p["position"]![0]!).ThenBy(p => (double)p["position"]![1]!).ToArray();
            for (int i = 0; i < ordered.Length; i++)
            {
                JToken point = ordered[i];
                if ((string?)point["page"] != c.Scene.Page.Page)
                { continue; }
                JToken? wanted = c.Targets["interruptions"]?.FirstOrDefault(p => (string?)p["name"] == (string?)point["name"] && c.Near(p["position"]!.ToObject<double[]>()!, point["position"]!.ToObject<double[]>()!));
                JToken? peer = (i ^ 1) < ordered.Length ? ordered[i ^ 1] : null;
                if (wanted?["counterpart"] is JObject expected && (peer is null || (string?)expected["page"] != (string?)peer["page"] || !c.Near(expected["position"]!.ToObject<double[]>()!, peer["position"]!.ToObject<double[]>()!)))
                { c.Add("interruption_pairing", "error", point["position"]!.ToObject<double[]>()!, [point["locator"]!.ToObject<DrawingLocator>()!], "A65 adjacent pairing differs from the target counterpart.", "eplan_placement_update", new() { ["locator"] = point["locator"]!.DeepClone() }); }
            }
        }
    }
}

/// <summary>cable_cores: an expected cable's cores differ from the cores read, in ends, label or colour, or share a definition point with another branch (A64).</summary>
internal static class CableCoresCheck
{
    private static readonly string[] _coreFields = ["label", "colour"];

    public static void Run(ReviewContext c)
    {
        if (!c.Enabled("cable_cores") || c.Targets["cables"] is not JArray cables)
        { return; }
        JObject actual = c.Actual;
        foreach (JToken cable in cables)
        {
            JToken? found = actual["cables"]!.FirstOrDefault(read => CableDefinitionPlanner.SameName((string)cable["designation"]!, (string)read["designation"]!, c.Scene.Page.Page));
            foreach (JToken core in cable["cores"]!)
            {
                JToken? wire = found?["cores"]?.FirstOrDefault(w => c.SameEnds(core, w));
                if (wire is null || _coreFields.Any(k => core[k] is not null && !JToken.DeepEquals(core[k], wire[k])) || actual["connections"]!.Concat(found!["cores"]!).Any(w => !c.SameEnds(w, core) && w["definitionPoints"]!.Values<long>().Intersect(wire["definitionPoints"]!.Values<long>()).Any() && !cable["cores"]!.Any(t => c.SameEnds(t, w) && _coreFields.All(k => JToken.DeepEquals(t[k], core[k])))))
                {
                    // A73 (live 2026-11-02): moving the cable puts it on the branch but drops its core assignment and
                    // generation does not restore it, so the computed position is advice, never a move fix.
                    JObject move = PageReviewCableFix.Arguments(c.Scene.Page.GridSize, found, actual["connections"]!.FirstOrDefault(connection => c.SameEnds(core, connection)), (JArray)actual["connections"]!);
                    double[] target = move["moves"] is JArray { Count: 1 } moves ? [(double)moves[0]!["x_mm"]!, (double)moves[0]!["y_mm"]!] : [];
                    string message = target.Length == 2
                        ? FormattableString.Invariant($"Core membership differs; an A64 trunk segment shares its definition point across branches. The cable should cross the intended branch with its position at ({target[0]}, {target[1]}). Moving a cable drops its core assignment (A73) and no tool reassigns cores yet: draw the cable again across that branch with eplan_connection_create.")
                        : "Core membership differs. A parallel exclusive branch and complete crossing evidence are required for coordinates; redraw the cable across the intended branch.";
                    c.Add("cable_core_segment", "error", target, wire?["locator"] is JObject l ? [l.ToObject<DrawingLocator>()!] : [], message, "eplan_connection_create", new JObject());
                }
            }
        }
    }
}

/// <summary>eplan_messages: the messages EPLAN's own check run left for this page.</summary>
internal static class EplanMessagesCheck
{
    public static void Run(ReviewContext c)
    {
        if (!c.Enabled("eplan_messages"))
        { return; }
        foreach (JToken message in c.Actual["messages"]!)
        { c.Add("eplan_message", (string)message["severity"]!, [], message["objects"]!.ToObject<DrawingLocator[]>()!, (string)message["text"]!, "eplan_check_list", new() { ["project"] = c.Scene.Project.Name }); }
    }
}
