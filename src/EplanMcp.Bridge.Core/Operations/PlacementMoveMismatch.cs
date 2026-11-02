using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

internal static class PlacementMoveMismatch
{
    private const int Limit = 20;

    internal static BridgeErrorException Create(string check, string detail, ConnectionCreatePlanner.Scene before,
        ConnectionCreatePlanner.Scene expected, ConnectionCreatePlanner.Scene actual, JObject[] observed, object expectedFact, object actualFact)
    {
        var added = new JArray();
        var removed = new JArray();
        var changed = new JArray();
        Dictionary<string, Queue<int>> remaining = actual.Page.Placements.Select((p, i) => (Placement: p, Index: i))
            .GroupBy(p => Identity(p.Placement)).ToDictionary(g => g.Key, g => new Queue<int>(g.Select(p => p.Index)), StringComparer.Ordinal);
        Dictionary<string, Queue<DrawingPlacement>> originals = before.Page.Placements.GroupBy(Identity)
            .ToDictionary(g => g.Key, g => new Queue<DrawingPlacement>(g), StringComparer.Ordinal);
        foreach (DrawingPlacement want in expected.Page.Placements)
        {
            string key = Identity(want);
            DrawingPlacement? original = originals.TryGetValue(key, out Queue<DrawingPlacement>? prior) && prior.Count > 0 ? prior.Dequeue() : null;
            if (!remaining.TryGetValue(key, out Queue<int>? indices) || indices.Count == 0)
            { removed.Add(Describe(want)); continue; }
            int index = indices.Dequeue();
            if (!JToken.DeepEquals(Comparable(want, check == "count"), Comparable(actual.Page.Placements[index], check == "count")))
            {
                changed.Add(new JObject
                {
                    ["before"] = original is null ? null : Describe(original),
                    ["expected"] = Describe(want),
                    ["after"] = observed[index].DeepClone()
                });
            }
        }
        foreach (int index in remaining.Values.SelectMany(q => q).OrderBy(i => i))
        { added.Add(observed[index].DeepClone()); }
        JObject[] differences = Differences(BridgeJson.FromObject(expectedFact), BridgeJson.FromObject(actualFact), "").ToArray();
        var facts = new JObject
        {
            ["check"] = check,
            ["detail"] = detail,
            ["expectedCount"] = expected.Page.Placements.Count,
            ["actualCount"] = actual.Page.Placements.Count,
            ["addedTotal"] = added.Count,
            ["removedTotal"] = removed.Count,
            ["changedTotal"] = changed.Count,
            ["added"] = new JArray(added.Take(Limit)),
            ["removed"] = new JArray(removed.Take(Limit)),
            ["changed"] = new JArray(changed.Take(Limit)),
            ["differenceTotal"] = differences.Length,
            ["differences"] = new JArray(differences.Take(Limit))
        };
        string summary = FormattableString.Invariant($"{check}: {detail}; expected {expected.Page.Placements.Count}, actual {actual.Page.Placements.Count}; added {added.Count}, removed {removed.Count}, changed {changed.Count}");
        return new BridgeErrorException(ErrorReason.EplanOperationFailed,
            "Placement closure, following text, pins or connections differ from the approved move (" + summary + "). See metadata.moveMismatch.",
            metadata: new() { ["moveMismatch"] = facts.ToString(Formatting.None) });
    }

    internal static JObject Describe(DrawingPlacement placement)
    {
        var result = new JObject
        {
            ["kind"] = placement.Kind,
            ["type"] = placement.Locator.Type,
            ["oid"] = placement.Locator.Oid,
            ["location"] = new JArray(placement.Location),
            ["bounds"] = BridgeJson.FromObject(placement.Bounds)
        };
        foreach (string key in new[] { "name", "property_id", "displayed_property_id", "property_index" })
        { if (placement.Properties.TryGetValue(key, out string? value)) { result[key] = value; } }
        if (placement.Parent is { } parent)
        { result["parent"] = new JObject { ["type"] = parent.Type, ["oid"] = parent.Oid }; }
        return result;
    }

    private static string Identity(DrawingPlacement placement) => BridgeJson.FromObject(new object?[] { placement.Kind, placement.Locator.Type, placement.Locator.Oid,
        placement.Parent?.Type, placement.Parent?.Oid, placement.Properties.TryGetValue("property_id", out string? id) ? id : null,
        placement.Properties.TryGetValue("property_index", out string? index) ? index : null }).ToString(Formatting.None);

    private static JToken Comparable(DrawingPlacement placement, bool ignoreDigest)
    {
        JToken token = BridgeJson.FromObject(placement);
        // Digests change with an approved move; identity diagnostics must not treat that as a replacement.
        if (ignoreDigest)
        {
            ((JObject)token["locator"]!).Remove("digest");
            if (token["parent"] is JObject parent)
            { parent.Remove("digest"); }
        }
        return token;
    }

    private static IEnumerable<JObject> Differences(JToken? expected, JToken? actual, string path)
    {
        if (JToken.DeepEquals(expected, actual))
        { yield break; }
        if (expected is JObject want && actual is JObject found)
        {
            foreach (string name in want.Properties().Select(p => p.Name).Union(found.Properties().Select(p => p.Name), StringComparer.Ordinal))
            {
                foreach (JObject difference in Differences(want[name], found[name], path + "/" + name.Replace("~", "~0").Replace("/", "~1")))
                { yield return difference; }
            }
        }
        else if (expected is JArray before && actual is JArray after)
        {
            for (int i = 0; i < Math.Max(before.Count, after.Count); i++)
            {
                foreach (JObject difference in Differences(i < before.Count ? before[i] : null, i < after.Count ? after[i] : null, path + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                { yield return difference; }
            }
        }
        else
        { yield return new JObject { ["path"] = path, ["expected"] = expected?.DeepClone(), ["actual"] = actual?.DeepClone() }; }
    }
}
