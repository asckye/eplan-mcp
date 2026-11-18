using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A54/A57: read-only absolute planning; property children share their parent's identifier, never its digest.</summary>
public static partial class PlacementMovePlanner
{
    public static JObject Plan(string json, ConnectionCreatePlanner.Scene scene, string? expectedFingerprint = null)
    {
        try
        { return Predict(json, scene, expectedFingerprint); }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
    }

    private static JObject Predict(string json, ConnectionCreatePlanner.Scene scene, string? expectedFingerprint)
    {
        Require(System.Text.Encoding.UTF8.GetByteCount(json) <= 256 * 1024, "", "Keep requests within 256 KiB.");
        JObject input;
        try
        {
            using var parser = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            input = JObject.Load(parser, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Require(!parser.Read(), "", "Supply one JSON object.");
        }
        catch (JsonReaderException) { throw WriteArguments.Invalid(["/: Supply strict JSON without duplicate members."]); }
        if (input.ContainsKey("property_moves"))
        { return PropertyPlacementMoves.Plan(input, scene, expectedFingerprint); }
        return new Prediction(input, scene).Run(expectedFingerprint);
    }

    internal static bool IsCablePoint(DrawingPlacement point, DrawingPlacement cable) => point.Kind == "ConnectionDefinitionPoint"
        && cable.Properties.TryGetValue("name", out string? name) && !string.IsNullOrWhiteSpace(name)
        && point.Properties.TryGetValue("name", out string? owner) && string.Equals(name, owner, StringComparison.Ordinal);

    /// <summary>
    /// Live 2026-11-02 (A75): at a moved cable's new crossing EPLAN adds a point with a new identity whose name is only a
    /// structure prefix ("+") until generation assigns it; any other name or position stays a mismatch.
    /// </summary>
    internal static bool IsNewCablePoint(DrawingPlacement point, IReadOnlyList<DrawingPlacement> before, IEnumerable<DrawingPlacement> cables)
    {
        const double tolerance = 0.5;
        string name = point.Properties.TryGetValue("name", out string? value) ? value ?? "" : "";
        return point.Kind == "ConnectionDefinitionPoint" && point.Location.Length == 2 && name.All(c => "=+&#-".Contains(c))
            && !before.Any(p => p.Locator.Type == point.Locator.Type && p.Locator.Oid == point.Locator.Oid)
            && cables.Any(c => c.Bounds is { Length: 4 } b && point.Location[0] >= b[0] - tolerance && point.Location[0] <= b[2] + tolerance
                && point.Location[1] >= b[1] - tolerance && point.Location[1] <= b[3] + tolerance);
    }

    public static Dictionary<string, double[][]> Autoconnect(Dictionary<string, SymbolConnectionPoint> pins)
    {
        string Owner(string pin) => string.Join("/", pin.Split('/').Take(2));
        return pins.SelectMany(a => pins.Where(b => a.Key != b.Key && Owner(a.Key) != Owner(b.Key)
            && Facing(a.Value, b.Value)).OrderBy(b => Math.Abs(b.Value.Location[0] - a.Value.Location[0]) + Math.Abs(b.Value.Location[1] - a.Value.Location[1])).Take(1).Where(b => b.Value.Direction == Opposite(a.Value.Direction))
            .Select(b => (Key: string.Join("|", new[] { a.Key, b.Key }.OrderBy(k => k, StringComparer.Ordinal)), Points: new[] { a.Value.Location, b.Value.Location })))
            .GroupBy(l => l.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Points, StringComparer.Ordinal);
    }
    private static bool Same(DrawingLocator a, DrawingLocator b) => ProjectResolver.SamePath(a.Project, b.Project) && a.Page == b.Page && a.Type == b.Type && a.Oid == b.Oid;
    private static string Opposite(string d) => d switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", "Right" => "Left", _ => "" };
    private static bool Facing(SymbolConnectionPoint a, SymbolConnectionPoint b) => a.Direction switch { "Up" => a.Location[0] == b.Location[0] && a.Location[1] < b.Location[1], "Down" => a.Location[0] == b.Location[0] && a.Location[1] > b.Location[1], "Left" => a.Location[1] == b.Location[1] && a.Location[0] > b.Location[0], _ => a.Location[1] == b.Location[1] && a.Location[0] < b.Location[0] };
    private static void Require(bool valid, string at, string message) { if (!valid) { throw WriteArguments.Invalid([at + ": " + message]); } }
    private static BridgeErrorException Missing(string message) => new(ErrorReason.CapabilityMissing, message, metadata: new Dictionary<string, string> { ["missing"] = "placement.update.execute" });
    private static BridgeErrorException Stale(string message) => new(ErrorReason.PlanStale, message);
}
