using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static EplanMcp.Bridge.Core.Operations.ConnectionCreatePlanner;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A55/S43: predict explicit cable membership; execution remains unavailable until T8b.</summary>
public static class CableDefinitionPlanner
{
    public sealed class Inventory
    {
        public bool Complete { get; set; }
        public List<string> Names { get; set; } = [];
        public List<Wire> Wires { get; set; } = [];
    }
    public sealed class Wire
    {
        public DrawingLocator Locator { get; set; } = new();
        public double[][] Points { get; set; } = [];
        public string[] Pins { get; set; } = [];
        public string? Cable { get; set; }
        public string? Label { get; set; }
        public string? Colour { get; set; }
        public string? CrossSection { get; set; }
    }
    public const string Colours = "BK BN RD OG YE GN BU VT GY WH PK TQ GNYE";

    internal static string Add(JObject input, Scene scene, JObject result, Func<JToken?, string, double[]> point)
    {
        var items = (JArray)input["items"]!;
        if (!items.Any(i => (string?)i["kind"] == "cable_definition"))
        { return ""; }
        var definitions = new JArray();
        var impact = new JArray();
        var keys = new HashSet<(string, string)>();
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<(JObject Reference, double[][] Points, Wire? Before)>();
        for (int i = 0; i < items.Count; i++)
        {
            if ((string?)items[i]["kind"] != "wire")
            { continue; }
            double grid = scene.Page.GridSize;
            double[][] points = items[i]["points"]!.Select(p => p.Select(v => DrawingCoordinates.Snap((double)v, grid, (bool?)input["snap_to_grid"] ?? true)).ToArray()).ToArray();
            string PinKey(JToken pin) => FormattableString.Invariant($"{pin["locator"]!["type"]}/{pin["locator"]!["oid"]}/{pin["pin_index"]}");
            string[] pins = [PinKey(items[i]["from"]!), PinKey(items[i]["to"]!)];
            if (!result["logicalAfterGenerate"]!.Any(g => g["pins"]!.Values<string>().OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(pins.OrderBy(p => p, StringComparer.Ordinal)) && (bool?)g["potentialStartIsNull"] == false))
            { throw Missing(); }
            candidates.Add((new JObject { ["item_index"] = i }, points, null));
        }
        for (int i = 0; i < items.Count; i++)
        {
            JToken item = items[i];
            if ((string?)item["kind"] != "cable_definition")
            { continue; }
            string at = "/items/" + i.ToString(CultureInfo.InvariantCulture);
            Members(item, at, "kind points designation type_text core_count cross_section_mm2 cores", "");
            Require(item["points"] is JArray { Count: 2 }, at + "/points", "Supply the definition line's two points.");
            double[][] line = item["points"]!.Select((p, n) => point(p, at + "/points/" + n.ToString(CultureInfo.InvariantCulture))).ToArray();
            Require((line[0][0] == line[1][0]) != (line[0][1] == line[1][1]), at + "/points", "Supply a nonzero orthogonal definition line.");
            string designation = Text(item["designation"], at + "/designation", 128);
            int separator = designation.IndexOf('-');
            Require(designation[0] is '-' or '=' or '+' && separator >= 0 && separator + 1 < designation.Length && char.IsLetterOrDigit(designation[separator + 1]) && designation.All(c => char.IsLetterOrDigit(c) || "=+-_.".Contains(c)), at + "/designation", "Use an explicit literal cable designation, for example -W3.");
            Require(names.Add(designation), at + "/designation", "Do not create the same cable twice.");
            Text(item["type_text"], at + "/type_text", 200);
            Require(item["core_count"]?.Type == JTokenType.Integer && (double)item["core_count"]! is >= 1 and <= 50, at + "/core_count", "Supply a declared count in 1..50.");
            Require(item["cross_section_mm2"]?.Type is JTokenType.Integer or JTokenType.Float && (double)item["cross_section_mm2"]! > 0, at + "/cross_section_mm2", "Supply a positive finite cross-section in mm2.");
            Require(item["cores"] is JArray { Count: >= 1 and <= 50 } cores && cores.Count <= (int)item["core_count"]!, at + "/cores", "Supply 1..50 cores without exceeding the declared count.");
            foreach (JToken core in (JArray)item["cores"]!)
            {
                string path = at + "/cores/" + ((JArray)item["cores"]!).IndexOf(core).ToString(CultureInfo.InvariantCulture);
                Members(core, path, "connection label colour", "");
                string label = Text(core["label"], path + "/label", 32);
                Require(label.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-')
                    && (label[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' || int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 1 && number <= (int)item["core_count"]! && label == number.ToString(CultureInfo.InvariantCulture)), path + "/label", "Use a literal label or a canonical core number within the declared count.");
                Require(keys.Add((designation, label)), path + "/label", "Duplicate cable designation + core label.");
                Require(core["colour"]?.Type == JTokenType.String && Colours.Split(' ').Contains((string)core["colour"]!, StringComparer.Ordinal), path + "/colour", "Use an explicit colour code: " + Colours + ".");
                Require(impact.Count < 50, path, "At most 50 cores are allowed across the entire call.");
                Members(core["connection"], path + "/connection", core["connection"] is JObject connection && connection["item_index"] is not null ? "item_index" : "locator", "");
                if (core["connection"]!["item_index"] is { } reference)
                { Require(reference.Type == JTokenType.Integer && (double)reference >= 0 && (double)reference < items.Count && (string?)items[(int)reference]["kind"] == "wire", path + "/connection/item_index", "Reference a wire item in this call, including forward references."); }
                else
                {
                    JToken locator = core["connection"]!["locator"]!;
                    Members(locator, path + "/connection/locator", "project page type oid digest", "");
                    Require(locator["project"]?.Type == JTokenType.String && locator["page"]?.Type == JTokenType.String && locator["type"]?.Type == JTokenType.Integer && (double)locator["type"]! is > 0 and <= int.MaxValue
                        && locator["oid"]?.Type == JTokenType.Integer && long.TryParse(locator["oid"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long oid) && oid > 0
                        && locator["digest"]?.Type == JTokenType.String && ((string)locator["digest"]!).Length == 64 && ((string)locator["digest"]!).All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9'), path + "/connection/locator", "Use an observed connection locator with positive type/oid and lowercase SHA-256 digest.");
                }
                impact.Add(new JObject { ["connection"] = core["connection"]!.DeepClone(), ["designation"] = designation, ["label"] = label, ["colour"] = core["colour"]!.DeepClone(), ["crossSectionMm2"] = item["cross_section_mm2"]!.DeepClone() });
            }
            var definition = (JObject)item.DeepClone();
            definition["points"] = JArray.FromObject(line);
            definitions.Add(definition);
        }
        Inventory inventory = scene.ReadCables?.Invoke() ?? throw Missing();
        if (!inventory.Complete || inventory.Wires.Count > 10000)
        { throw Missing(); }
        Require(!inventory.Names.Any(n => names.Any(d => n == d || d.StartsWith("-", StringComparison.Ordinal) && n.EndsWith(d, StringComparison.Ordinal))), "/items", "A cable designation already exists; choose an explicit unused name.");
        candidates.AddRange(inventory.Wires.Select(w => (new JObject { ["locator"] = BridgeJson.FromObject(w.Locator) }, w.Points, (Wire?)w)));
        foreach (JToken row in impact)
        {
            var matches = candidates.Where(c => JToken.DeepEquals(c.Reference, row["connection"])).ToArray();
            if (matches.Length != 1)
            { throw new BridgeErrorException(ErrorReason.PlanStale, "The connection locator changed; read the page and re-plan."); }
            var selected = matches[0];
            Require(assigned.Add(selected.Reference.ToString(Formatting.None)), "/items", "Assign each connection only once.");
            if (selected.Before is { } old && (!string.IsNullOrEmpty(old.Cable) || old.Locator.Project != scene.Project.Path || old.Locator.Page != scene.Page.Page))
            { throw Missing(); }
            row["before"] = selected.Before is null ? JValue.CreateNull() : BridgeJson.FromObject(selected.Before);
        }
        string Segment(double[][] pair) => string.Join("|", pair.Select(p => new JArray(p).ToString(Formatting.None)).OrderBy(p => p, StringComparer.Ordinal));
        var knownSegments = new HashSet<string>(candidates.SelectMany(w => w.Points.Zip(w.Points.Skip(1), (a, b) => Segment([a, b]))), StringComparer.Ordinal);
        var allSegments = scene.Graphical.Concat(result["graphicalAutoconnect"]!.ToObject<List<double[][]>>()!).ToArray();
        foreach (JToken definition in definitions)
        {
            double[][] line = definition["points"]!.ToObject<double[][]>()!;
            var crossed = candidates.Where(c => Crossings(line, c.Points) > 0).ToArray();
            JToken[] expected = impact.Where(r => (string?)r["designation"] == (string?)definition["designation"]).Select(r => r["connection"]!).ToArray();
            if (crossed.Any(c => Crossings(line, c.Points) != 1) || crossed.Length != expected.Length || crossed.Any(c => !expected.Any(e => JToken.DeepEquals(e, c.Reference)))
                || allSegments.Any(s => Crossings(line, s) > 0 && !knownSegments.Contains(Segment(s))))
            { throw Missing(); }
        }
        result["cableDefinitions"] = definitions;
        result["coreImpact"] = impact;
        result["generation"] = "A55: wires -> generate -> definition line -> generate -> bind -> generate -> cores. Execution requires T8b.";
        result["undoSteps"] = JValue.CreateNull();
        result["executionCapability"] = "CAPABILITY_MISSING";
        return Fingerprint.Of(inventory.Names.Select(n => "name:" + n).Concat(inventory.Wires.Select(w => BridgeJson.FromObject(w).ToString(Formatting.None))));
    }
    private static int Crossings(double[][] line, double[][] points) => points.Zip(points.Skip(1), (a, b) =>
    {
        bool vertical = line[0][0] == line[1][0];
        if (Math.Min(a[0], b[0]) > Math.Max(line[0][0], line[1][0]) || Math.Max(a[0], b[0]) < Math.Min(line[0][0], line[1][0]) || Math.Min(a[1], b[1]) > Math.Max(line[0][1], line[1][1]) || Math.Max(a[1], b[1]) < Math.Min(line[0][1], line[1][1]))
        { return 0; }
        if (vertical == (a[0] == b[0]))
        { return 2; }
        double[] crossing = vertical ? [line[0][0], a[1]] : [a[0], line[0][1]];
        return line.Concat([a, b]).Any(p => p.SequenceEqual(crossing)) ? 2 : 1;
    }).Sum();
    private static string Text(JToken? token, string at, int limit)
    {
        Require(token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)token) && ((string)token!).Length <= limit && !((string)token!).Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format), at, "Supply nonempty bounded text without controls.");
        return (string)token!;
    }
    private static BridgeErrorException Missing() => new(ErrorReason.CapabilityMissing, "Read complete, unbranched same-page wire geometry and unused cable names; the definition line must cross exactly the explicit, unassigned cores once.", metadata: new Dictionary<string, string> { ["missing"] = "connection.create.cable.execute" });
}
