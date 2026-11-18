using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A52: complete-page prediction before either committed placement or generation.</summary>
public static partial class ConnectionCreatePlanner
{
    public static JObject Plan(string json, PageScene scene, ISymbolReader reader, string? expectedFingerprint = null)
    {
        try
        { return Predict(json, scene, reader, expectedFingerprint); }
        catch (BridgeErrorException ex)
        {
            if (expectedFingerprint is not null && json.IndexOf("\"cable_definition\"", StringComparison.Ordinal) >= 0 && ex.Error.Reason != ErrorReason.PlanStale)
            { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PlanStale, "Planning evidence changed; create a new dry-run plan.")); }
            ex.Error.Execution = ExecutionState.NotStarted;
            throw;
        }
    }

    private static JObject Predict(string json, PageScene scene, ISymbolReader reader, string? expectedFingerprint)
        => new Prediction(Parse(json), scene, reader).Run(expectedFingerprint);

    private static JObject Parse(string json)
    {
        Require(System.Text.Encoding.UTF8.GetByteCount(json) <= 256 * 1024, "", "Keep requests within 256 KiB.");
        JObject input;
        try
        {
            using var parser = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            input = JObject.Load(parser, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Require(!parser.Read(), "", "Supply one JSON object.");
        }
        catch (JsonReaderException ex) { throw WriteArguments.Invalid(["/" + (ex.Path ?? "").Replace("[", "/").Replace("]", "").Replace(".", "/") + ": Supply strict JSON without duplicate members."]); }
        return input;
    }

    /// <summary>
    /// Directional T-node (EPLAN help: the undesignated point is the source, target 1 the straight line, target 2 the diagonal;
    /// the SPECIAL T-nodes list their points as [target 1, source, target 2]). As on the official EES pages (TLRU/0, TLRO/0)
    /// the source is where the potential comes from, target 1 the branch and target 2 the continuing line; without a source,
    /// current runs left to right and top to bottom (A115). Returns null when the directions are not a T (reported elsewhere).
    /// </summary>
    private static readonly string[] _cardinals = ["Up", "Down", "Left", "Right"];

    internal static (int Number, bool Explicit, string Pointer)? DirectionalVariant(string[] directions, JToken? source, bool hasVariant, string at)
    {
        Require(source is null || !hasVariant, at + "/source", "Give either variant or source, not both.");
        Require(source is null || source.Type == JTokenType.String, at + "/source", "Supply source as Up, Down, Left or Right.");
        string[] missing = [.. _cardinals.Where(d => !directions.Contains(d, StringComparer.Ordinal))];
        if (hasVariant || directions.Length != 3 || directions.Distinct(StringComparer.Ordinal).Count() != 3 || missing.Length != 1)
        { return null; }
        string branch = Opposite(missing[0]);
        bool throughIsHorizontal = branch is "Up" or "Down";
        string from = source is null ? (throughIsHorizontal ? "Left" : "Up") : (string)source!;
        Require(from != branch && directions.Contains(from, StringComparer.Ordinal), at + "/source",
            "The source is one end of the through line: " + string.Join(" or ", directions.Where(d => d != branch)) + ".");
        int number = throughIsHorizontal ? (from == "Left" ? 0 : 1) : (from == "Up" ? 2 : 3);
        return (number, source is not null, at + "/source");
    }

    private static string Opposite(string d) => d switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", "Right" => "Left", _ => "" };
    private static string SegmentKey(double[][] segment) => string.Join("|", segment.Select(FormatPoint).OrderBy(p => p, StringComparer.Ordinal));
    internal static IEnumerable<double[][]> ExpectedGraphical(PageScene scene, JObject plan)
    {
        var split = new HashSet<string>(plan["splitSegments"]?.Select(s => SegmentKey(s["segment"]!.ToObject<double[][]>()!)) ?? [], StringComparer.Ordinal);
        return scene.Graphical.Where(s => !split.Contains(SegmentKey(s))).Concat(plan["graphicalAutoconnect"]!.ToObject<List<double[][]>>()!);
    }
    private static string Direction(double[] a, double[] b) => a[0] == b[0] ? b[1] > a[1] ? "Up" : "Down" : b[0] > a[0] ? "Right" : "Left";
    private static double Distance(double[] a, double[] b) => Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]);
    private static double[] Box(double[][] s) => [Math.Min(s[0][0], s[1][0]), Math.Min(s[0][1], s[1][1]), Math.Max(s[0][0], s[1][0]), Math.Max(s[0][1], s[1][1])];
    /// <summary>
    /// Two lines that cross at right angles, neither ending on the other: EPLAN joins nothing there. The sample project's
    /// supply rails cross the drops to their breakers this way (api-notes A114).
    /// </summary>
    internal static bool Crossing(double[][] a, double[][] b) => (a[0][0] == a[1][0]) != (b[0][0] == b[1][0]) && !a.Any(p => On(p, b)) && !b.Any(p => On(p, a));

    internal static bool On(double[] p, double[][] s) => p[0] >= Box(s)[0] && p[0] <= Box(s)[2] && p[1] >= Box(s)[1] && p[1] <= Box(s)[3];
    internal static bool Intersects(double[][] a, double[][] b) => Box(a)[0] <= Box(b)[2] && Box(a)[2] >= Box(b)[0] && Box(a)[1] <= Box(b)[3] && Box(a)[3] >= Box(b)[1];
    internal static void Require(bool valid, string at, string message) { if (!valid) { throw WriteArguments.Invalid([at + ": " + message]); } }
    // Names the first failed scene check so the caller can tell which page object blocks drawing.
    private static string? SceneGap(PageScene scene, bool checkPins = true)
    {
        if (!scene.Complete)
        { return "a page placement has no drawing record"; }
        if (scene.PageType is not ("Circuit" or "CircuitSingleLine"))
        { return "page type " + scene.PageType; }
        if (scene.Page.Project != scene.Project.Path || scene.Page.PageSize is null)
        { return "page geometry is unavailable"; }
        if (!checkPins)
        { return null; }
        if (scene.Pins.Count > 10000)
        { return "more than 10000 pins"; }
        if (scene.Pins.FirstOrDefault(p => p.Value.Location.Length != 2 || p.Value.Location.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 10000) || Opposite(p.Value.Direction) == ""
            || !DrawingCoordinates.Contains(scene.Page.PageSize, [[p.Value.Location[0], p.Value.Location[1], p.Value.Location[0], p.Value.Location[1]]])) is { Key: not null } pin)
        { return "pin " + pin.Key + " at " + FormatPoint(pin.Value.Location) + " direction " + pin.Value.Direction + " is outside the page or undirected"; }
        // Acceptance trial: corner and T-node symbols keep all their pins at the node centre; only pins of different
        // placements may not coincide.
        if (scene.Pins.GroupBy(p => FormatPoint(p.Value.Location), StringComparer.Ordinal).FirstOrDefault(g => g.Select(p => Owner(p.Key)).Distinct(StringComparer.Ordinal).Count() > 1) is { } shared)
        { return "pins " + string.Join(", ", shared.Select(p => p.Key)) + " share " + shared.Key; }
        return null;
    }

    // Acceptance trial: a location box is a dashed frame around devices; wires and nodes cross and sit inside it.
    private static bool Obstacle(DrawingPlacement placement) => placement.Kind != "LocationBox" && LayoutGuard.Positioned(placement) && DrawingCoordinates.ValidBounds(placement.Bounds);

    private static string Owner(string pin) => string.Join("/", pin.Split('/').Take(2));
    // Nodes are SymbolReference placements (devices are Function/Terminal); older evidence knows them by all pins at one point.
    internal static HashSet<string> NodeOwners(IEnumerable<(string Pin, double[] Location)> pins, IEnumerable<string> symbolOwners)
    {
        var references = new HashSet<string>(symbolOwners, StringComparer.Ordinal);
        return new HashSet<string>(pins.GroupBy(p => Owner(p.Pin), StringComparer.Ordinal)
            .Where(g => g.Count() > 1 && (references.Contains(g.Key) || g.Select(p => FormattableString.Invariant($"{p.Location[0]},{p.Location[1]}")).Distinct(StringComparer.Ordinal).Count() == 1))
            .Select(g => g.Key), StringComparer.Ordinal);
    }

    private static string FormatPoint(double[] location) => string.Join(",", location.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

    private static BridgeErrorException Missing(string message) => new(ErrorReason.CapabilityMissing, message, metadata: new Dictionary<string, string> { ["missing"] = "connection.create.execute" });
    internal static void Members(JToken? token, string at, string required, string optional)
    {
        Require(token is JObject, at, "Supply an object.");
        string[] names = required.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        foreach (string name in names)
        { Require(token![name] is not null && token[name]!.Type != JTokenType.Null, at + "/" + name, "Supply this required member."); }
        foreach (JProperty p in ((JObject)token!).Properties())
        { Require(names.Concat(optional.Split([' '], StringSplitOptions.RemoveEmptyEntries)).Contains(p.Name, StringComparer.Ordinal), at + "/" + p.Name.Replace("~", "~0").Replace("/", "~1"), "Omit unknown members."); }
    }
    internal static void FiniteNumbers(JToken token, string at)
    {
        Require(token.Type is not (JTokenType.Float or JTokenType.Integer) || double.TryParse(token.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsInfinity(number) && !double.IsNaN(number), at, "Supply a finite number.");
        if (token is JObject obj)
        { foreach (JProperty p in obj.Properties()) { FiniteNumbers(p.Value, at + "/" + p.Name.Replace("~", "~0").Replace("/", "~1")); } }
        else if (token is JArray array)
        { for (int i = 0; i < array.Count; i++) { FiniteNumbers(array[i], at + "/" + i.ToString(CultureInfo.InvariantCulture)); } }
    }
}
