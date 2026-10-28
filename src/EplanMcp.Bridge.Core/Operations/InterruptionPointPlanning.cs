using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A55/S43: explicit page closure, native pairing and computed reference text; never search the project.</summary>
public static class InterruptionPointPlanning
{
    private static readonly string[] _identity = ["page", "name", "position", "pin", "direction"];
    public static string[] Pages(JObject plan) => plan["affectedPages"]!.Values<string>().Select(p => p!).ToArray();
    public static void Plan(JObject plan, ConnectionCreatePlanner.Scene scene, IConnectionCreator reader)
    {
        JToken[] requested = plan["symbols"]!.Where(s => s["interruption"] is not null).ToArray();
        string[] pages = requested.SelectMany(s => s["interruption"]!["counterpart_pages"]!.Values<string>()).Select(p => p!).Append(scene.Page.Page).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Require(pages.Length <= 21, "Keep the combined counterpart scope within 20 pages plus the target page.");
        plan["affectedPages"] = new JArray(pages);
        JObject scope = reader.Interruptions(scene.Project.Path, pages);
        Require(scope["pages"]!.Sum(p => p["drawing"]!["placements"]!.Count()) <= 10000 && scope["points"]!.Count() <= 1000, "Keep complete scoped evidence within 10000 placements and 1000 interruption points.");
        var projected = (JArray)scope["points"]!.DeepClone();
        foreach (JToken row in requested)
        {
            JToken item = row["interruption"]!;
            string name = (string)item["name"]!;
            Require(!projected.Any(p => (string?)p["name"] == name && (string?)p["page"] == scene.Page.Page), "A business name may occur only once on the target page.");
            projected.Add(new JObject { ["name"] = name, ["page"] = scene.Page.Page, ["position"] = row["position"]!.DeepClone(), ["pin"] = new JArray(row["position"]!.Values<double>().Zip(row["symbol"]!["connectionPoints"]![0]!["location"]!.Values<double>(), (a, b) => a + b)), ["direction"] = item["direction"]!.DeepClone() });
            JToken[] peers = projected.Where(p => (string?)p["name"] == name && (string?)p["page"] != scene.Page.Page).ToArray();
            Require(peers.Length <= 1 && peers.All(p => item["counterpart_pages"]!.Values<string>().Contains((string?)p["page"], StringComparer.Ordinal)), "Pairing is ambiguous or outside counterpart_pages; narrow or correct the explicit scope.");
            Require(peers.All(p => !p["counterparts"]!.Any()), "The counterpart is already paired; do not extend an existing pair.");
        }
        plan["interruptionScope"] = scope;
        Require(scope["points"]!.All(p => p["counterparts"]!.All(c => pages.Contains((string?)c["page"], StringComparer.Ordinal))), "Explicit pages do not cover the native counterpart closure.");
        Require(projected.GroupBy(p => (string?)p["name"], StringComparer.Ordinal).All(g => g.Count() <= 2), "More than one counterpart makes pairing ambiguous.");
        // Join page-local autoconnect graphs through the shared business name, retaining real function-pin identities.
        var groups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string Vertex(string page, JToken point) => page + ":" + point.ToString(Formatting.None);
        HashSet<string> Group(string key)
        { if (!groups.TryGetValue(key, out HashSet<string>? g)) { groups[key] = g = new(StringComparer.Ordinal) { key }; } return g; }
        void Join(string a, string b)
        { var union = new HashSet<string>(Group(a).Concat(Group(b)), StringComparer.Ordinal); foreach (string key in union) { groups[key] = union; } }
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JToken page in scope["pages"]!)
        {
            string name = (string)page["page"]!;
            foreach (JProperty pin in ((JObject)page["pins"]!).Properties())
            { string key = Vertex(name, pin.Value["location"]!); Group(key); pins.Add(pin.Name, key); }
            IEnumerable<JToken> lines = page["graphical"]!;
            if (name == scene.Page.Page)
            { lines = lines.Concat(plan["graphicalAutoconnect"]!); }
            foreach (JToken line in lines)
            { Join(Vertex(name, line[0]!), Vertex(name, line[1]!)); }
        }
        foreach (var pair in projected.GroupBy(p => (string)p["name"]!, StringComparer.Ordinal))
        {
            JToken first = pair.First();
            foreach (JToken point in pair)
            { Join(Vertex((string)first["page"]!, first["pin"]!), Vertex((string)point["page"]!, point["pin"]!)); }
        }
        var logical = new JArray();
        var affectedPins = new HashSet<string>(StringComparer.Ordinal);
        foreach (HashSet<string> group in groups.Values.Distinct())
        {
            string[] ends = pins.Where(p => group.Contains(p.Value)).Select(p => p.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (projected.Any(p => group.Contains(Vertex((string)p["page"]!, p["pin"]!)) && requested.Any(r => (string?)r["interruption"]!["name"] == (string?)p["name"])))
            { affectedPins.UnionWith(ends); }
            bool potential = projected.Any(p => group.Contains(Vertex((string)p["page"]!, p["pin"]!)) && projected.Count(q => (string?)q["name"] == (string?)p["name"]) == 1);
            potential |= plan["logicalAfterGenerate"]!.Any(g => (bool)g["potentialStartIsNull"]! && g["pins"]!.Values<string>().Any(p => ends.Contains(p, StringComparer.Ordinal)));
            potential |= scope["pages"]!.Any(p => p["logical"]!.Any(c => c["startDevice"]!.Type == JTokenType.Null && ends.Contains((string?)c["endDevice"], StringComparer.Ordinal))) && !projected.Any(p => group.Contains(Vertex((string)p["page"]!, p["pin"]!)));
            if (ends.Length > 1 || potential && ends.Length > 0)
            { logical.Add(new JObject { ["pins"] = new JArray(ends), ["potentialStartIsNull"] = potential }); }
        }
        plan["logicalAfterGenerate"] = logical;
        scope["preservedEndpoints"] = new JArray(scope["pages"]!.SelectMany(p => p["logical"]!).Where(c => !affectedPins.Contains((string?)c["startDevice"] ?? "") && !affectedPins.Contains((string?)c["endDevice"] ?? "")).Select(c => new JArray(c["startDevice"]!, c["endDevice"]!)));
    }

    public static JArray Verify(JObject plan, JObject actual)
    {
        var result = new JArray();
        JToken[] before = plan["interruptionScope"]!["points"]!.ToArray();
        Require(actual["points"]!.Count() == before.Length + plan["symbols"]!.Count(s => s["interruption"] is not null), "The scoped interruption inventory changed unexpectedly.");
        foreach (JToken point in before)
        {
            JToken? found = actual["points"]!.SingleOrDefault(p => (long?)p["oid"] == (long?)point["oid"]);
            Require(found is not null && _identity.All(k => JToken.DeepEquals(found[k], point[k])), "An existing counterpart changed identity, name or geometry.");
        }
        foreach (JToken row in plan["symbols"]!.Where(s => s["interruption"] is not null))
        {
            string name = (string)row["interruption"]!["name"]!;
            JToken[] pair = actual["points"]!.Where(p => (string?)p["name"] == name).ToArray();
            Require(pair.Length is 1 or 2, "Native pairing is missing or ambiguous.");
            JToken placed = pair.Single(p => (string?)p["page"] == (string?)plan["page"]);
            Require(JToken.DeepEquals(placed["position"], row["position"]) && (string?)placed["direction"] == (string?)row["interruption"]!["direction"], "Interruption name, position or direction readback differs.");
            foreach (JToken point in pair)
            {
                JToken[] peers = pair.Where(p => !ReferenceEquals(p, point)).ToArray();
                Require(point["counterparts"]!.Count() == peers.Length && peers.All(p => point["counterparts"]!.Any(c => (string?)c["page"] == (string?)p["page"] && (long?)c["oid"] == (long?)p["oid"]))
                    && (peers.Length == 0 || !string.IsNullOrWhiteSpace((string?)point["crossReference"])), "EPLAN computed an unexpected counterpart or empty paired cross-reference.");
                Require(peers.All(p => (string?)p["page"] == (string?)plan["page"] || row["interruption"]!["counterpart_pages"]!.Values<string>().Contains((string?)p["page"], StringComparer.Ordinal)), "EPLAN paired outside counterpart_pages.");
                result.Add(point.DeepClone());
            }
        }
        return result;
    }
    private static void Require(bool valid, string message)
    { if (!valid) { throw WriteArguments.Invalid(["/items: " + message]); } }
}
