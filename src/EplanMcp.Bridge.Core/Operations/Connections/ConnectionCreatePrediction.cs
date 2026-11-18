using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

public static partial class ConnectionCreatePlanner
{
    /// <summary>
    /// One prediction, in the order <see cref="Run"/> lists: what the request asks for, then the page as it would be after it.
    /// Each step reads what the steps before it left in the fields; the order of the checks is the order of the refusals.
    /// </summary>
    private sealed partial class Prediction(JObject input, Scene scene, ISymbolReader reader)
    {
        private readonly List<double[][]> _paths = [];
        private readonly HashSet<string> _endpointPins = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (double[] Point, string[] Directions, string Kind)> _nodes = new(StringComparer.Ordinal);
        private readonly JArray _requested = new();
        private readonly JArray _resolved = new();
        private readonly Dictionary<string, JObject> _interruptions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (int Number, string Pointer)> _requestedVariants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (int Number, bool Explicit, string Pointer)> _directionalVariants = new(StringComparer.Ordinal);
        private readonly JArray _definitions = new();
        // A83: EPLAN's insert command defaults to BP; callers may select any SPECIAL interruption symbol.
        private readonly Dictionary<string, string> _interruptionSymbols = new(StringComparer.Ordinal);
        private readonly List<(double[][] Segment, double[] Point)> _splits = [];
        // A64: a node variant may offset one pin; the drawn line leaves from that pin, not from the node point.
        private readonly Dictionary<string, double[]> _nodePins = new(StringComparer.Ordinal);
        private readonly JArray _logical = new();
        private List<SymbolSearchItem>? _variants;
        private PartsDatabaseContent? _partsContent;
        private string _guard = "";
        private bool _generate;
        private bool _snap;
        private double _grid;
        private double[][][] _existingSegments = [];
        private List<double[][]> _segments = [];
        private (double[] Point, string Direction)[] _ports = [];
        // T-nodes whose target order this prediction chose itself, with the other variants of the same symbol.
        private readonly List<((double[] Point, string[] Directions, string Kind) Node, int Index, SymbolSearchItem Symbol, SymbolSearchItem[] Others)> _chosenTNodes = [];
        private HashSet<string> _nodeCentres = [];
        private Dictionary<string, HashSet<string>> _groups = [];
        private HashSet<string> _nodeOwners = [];
        private List<double[][]> _drawn = [];

        public JObject Run(string? expectedFingerprint)
        {
            ReadOptions();
            ReadItems();
            AddBends();
            SplitExisting();
            ResolveNodes();
            BuildSegments();
            CheckSegments();
            GroupConnected();
            KeepChainsClosed();
            PredictLogical();
            JObject result = Result(Layout());
            return Seal(result, expectedFingerprint);
        }

        private void ReadOptions()
        {
            Members(input, "", "project page items", "dry_run snap_to_grid response_format plugin_sources layout_guard generate");
            _guard = LayoutGuard.Mode(input["layout_guard"]);
            Require(input["plugin_sources"] is null || input["plugin_sources"] is JArray sources
                && sources.All(source => source.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)source)), "/plugin_sources", "Supply plugin identities as text.");
            FiniteNumbers(input, "");
            Require(input["project"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["project"]), "/project", "Specify an open project.");
            Require(input["page"]?.Type == JTokenType.String && (string?)input["page"] == scene.Page.Page, "/page", "Select the exact scene page.");
            Require((string?)input["project"] == scene.Project.Name || (string?)input["project"] == scene.Project.Path, "/project", "Re-read the specified project.");
            foreach (string name in new[] { "dry_run", "snap_to_grid", "generate" })
            { Require(input[name] is null || input[name]!.Type == JTokenType.Boolean, "/" + name, "Supply a boolean."); }
            Require(input["response_format"] is null || input["response_format"]!.Type == JTokenType.String && (string?)input["response_format"] is "concise" or "detailed", "/response_format", "Use concise or detailed.");
            Require(input["items"] is JArray { Count: >= 1 and <= 50 }, "/items", "Supply 1..50 items.");
            _generate = (bool?)input["generate"] ?? true;
            if (!_generate)
            { Require(input["items"]!.All(i => i is JObject && (string?)i["kind"] is "corner" or "t_node" or "potential_point" or "interruption_point" or "potential_definition" or "autoconnect_break"), "/items", "generate:false accepts only corner, t_node, potential_point, interruption_point, potential_definition and autoconnect_break."); }
            else
            { Require(!input["items"]!.Any(i => i is JObject && (string?)i["kind"] == "autoconnect_break"), "/items", "An autoconnect_break changes which lines EPLAN draws: place it with generate:false, then call eplan_connection_generate."); }
            bool definitionsOnly = input["items"]!.All(i => i is JObject && (string?)i["kind"] is "definition_point" or "potential_definition");
            if (SceneGap(scene, _generate && !definitionsOnly) is { } gap)
            { throw Missing("Read a complete Circuit page inventory with verified bounds and pin geometry: " + gap + "."); }
            _snap = (bool?)input["snap_to_grid"] ?? true;
            _grid = scene.Page.GridSize;
            // Acceptance trial: library pins may be off grid (IEC_symbol X has pins at +2 and -0.75 mm), so only path
            // endpoints that equal their pin are exempt from the grid; bends and nodes stay on it.
            if (double.IsNaN(_grid) || double.IsInfinity(_grid) || _grid <= 0)
            { throw Missing("Read a valid page grid."); }
        }

        private PartsDatabaseContent ReadParts() => _partsContent ??= (reader as IPartsDatabase ?? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The parts database reader is unavailable.")).Read(null);

        private static string Key(double[] p) => FormattableString.Invariant($"{p[0]},{p[1]}");

        private double[] Point(JToken? token, string at)
        {
            Require(token is JArray { Count: 2 } a && a.All(n => n.Type is JTokenType.Integer or JTokenType.Float && (double)n is >= 0 and <= 10000), at, "Supply [x_mm,y_mm] in 0..10000 mm.");
            double[] actual = token!.Select(n => DrawingCoordinates.Snap((double)n!, _grid, _snap)).ToArray();
            Require(DrawingCoordinates.Contains(scene.Page.PageSize, [[actual[0], actual[1], actual[0], actual[1]]]), at, "Keep the path inside Page.Size.");
            _requested.Add(new JObject { ["pointer"] = at, ["requested"] = token!.DeepClone(), ["actual"] = new JArray(actual) });
            return actual;
        }

        // The exact, unsnapped location of a resolvable pin that the requested end point names; otherwise null.
        private double[]? PinPoint(JToken? pin, JToken asked)
        {
            if (asked is not JArray { Count: 2 } r || r.Any(n => n.Type is not (JTokenType.Integer or JTokenType.Float)) || pin is not JObject
                || pin["locator"] is not JObject locator || pin["pin_index"]?.Type != JTokenType.Integer
                || locator["type"]?.Type != JTokenType.Integer || locator["oid"]?.Type != JTokenType.Integer)
            { return null; }
            string key = FormattableString.Invariant($"{(int?)locator["type"]}/{(long?)locator["oid"]}/{(int)pin["pin_index"]!}");
            return scene.Pins.TryGetValue(key, out SymbolConnectionPoint? located) && located.Location.Length == 2
                && Math.Abs((double)r[0] - located.Location[0]) < 1e-9 && Math.Abs((double)r[1] - located.Location[1]) < 1e-9 ? located.Location : null;
        }

        private string Pin(JToken? token, string at)
        {
            Members(token, at, "locator pin_index", "");
            Members(token!["locator"], at + "/locator", "project page type oid digest", "");
            JObject locator = (JObject)token["locator"]!;
            Require(locator["project"]?.Type == JTokenType.String && locator["page"]?.Type == JTokenType.String && locator["digest"]?.Type == JTokenType.String
                && locator["type"]?.Type == JTokenType.Integer && locator["oid"]?.Type == JTokenType.Integer && token["pin_index"]?.Type == JTokenType.Integer, at, "Use a drawing locator and zero-based pin_index.");
            Require(((string)locator["digest"]!).Length == 64 && ((string)locator["digest"]!).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), at + "/locator/digest", "Use the observed lowercase SHA-256 digest.");
            Require(long.TryParse(locator["oid"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long oid) && oid > 0
                && int.TryParse(locator["type"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int type) && type > 0
                && int.TryParse(token["pin_index"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int pinIndex) && pinIndex >= 0, at, "Use positive type/oid and a nonnegative pin index.");
            DrawingLocator given = locator.ToObject<DrawingLocator>(BridgeJson.Serializer)!;
            if (!scene.Page.Placements.Any(p => DrawingLocators.Matches(given, p.Locator)))
            { throw new BridgeErrorException(ErrorReason.PlanStale, "The pin locator changed; read the page again."); }
            string key = FormattableString.Invariant($"{given.Type}/{given.Oid}/{token["pin_index"]}");
            Require(scene.Pins.ContainsKey(key), at, "Select an existing pin index from this page.");
            return key;
        }

        private void Node(double[] point, string[] directions, string kind, string at)
        {
            Require(directions.Distinct(StringComparer.Ordinal).Count() == directions.Length && directions.All(d => d is "Up" or "Down" or "Left" or "Right"), at, "Supply distinct cardinal directions.");
            Require(kind switch { "corner" => directions.Length == 2 && directions[0] != Opposite(directions[1]), "t_node" => directions.Length == 3, "potential_point" or "interruption_point" => directions.Length == 1, "autoconnect_break" => directions.Length == 4, _ => false }, at, "Directions must match the node kind.");
            Require(!_nodes.ContainsKey(Key(point)), at, "Duplicate nodes are not allowed.");
            _nodes.Add(Key(point), (point, directions, kind));
        }

        private void ReadItems()
        {
            int number = -1;
            foreach (JToken item in (JArray)input["items"]!)
            {
                number++;
                string at = "/items/" + number.ToString(CultureInfo.InvariantCulture);
                if (item is JObject && (string?)item["kind"] == "cable_definition")
                { continue; }
                Require(item is JObject && item["kind"]?.Type == JTokenType.String, at + "/kind", "Supply wire, corner, t_node, potential_point, interruption_point, autoconnect_break, cable_definition or definition_point.");
                switch ((string?)item["kind"])
                {
                    case "autoconnect_break":
                        Members(item, at, "kind position", "");
                        Node(Point(item["position"], at + "/position"), ["Down", "Left", "Right", "Up"], "autoconnect_break", at);
                        break;
                    case "potential_definition":
                        _definitions.Add(ConnectionDefinitionPoints.PlanPotential(item, at, Point(item["position"], at + "/position"), number));
                        break;
                    case "definition_point":
                        _definitions.Add(ConnectionDefinitionPoints.Plan(item, at, [], "", "", number, scene, _variants ??= reader.Read(scene.Project.Path, new() { Library = "SPECIAL" }), existing: true, parts: ReadParts));
                        break;
                    case "interruption_point":
                        ReadInterruption(item, at);
                        break;
                    case "wire":
                        ReadWire(item, at, number);
                        break;
                    default:
                        ReadNode(item, at);
                        break;
                }
            }
            Require(!_generate || _definitions.Count == 0 || _interruptions.Count == 0 && !input["items"]!.Any(i => (string?)i["kind"] == "cable_definition"), "/items", "Create definition points separately from interruption points and cable definitions to preserve one transaction and undo step.");
        }

        private void ReadInterruption(JToken item, string at)
        {
            Members(item, at, "kind name direction position", "counterpart_pages sort_code symbol symbol_variant property_arrangement");
            Require(item["property_arrangement"] is null || item["property_arrangement"]!.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)item["property_arrangement"]) && !((string)item["property_arrangement"]!).Any(char.IsControl), at + "/property_arrangement", "Supply an exact nonblank arrangement name without controls.");
            Require(item["sort_code"] is null || item["sort_code"]!.Type == JTokenType.Integer
                && long.TryParse(item["sort_code"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out _), at + "/sort_code", "Supply a nonnegative Int64 integer.");
            Require(item["name"]?.Type == JTokenType.String && ((string)item["name"]!).Length is >= 1 and <= 200 && !string.IsNullOrWhiteSpace((string?)item["name"])
                && !((string)item["name"]!).Any(char.IsControl), at + "/name", "Supply the shared name, 1..200 characters: 24V or -24V takes this page's higher-level function, =F-24V stays as written.");
            Require(item["direction"]?.Type == JTokenType.String, at + "/direction", "Supply Up, Down, Left or Right.");
            Require(item["counterpart_pages"] is null || item["counterpart_pages"] is JArray { Count: >= 1 and <= 20 } scope && scope.All(p => p.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)p))
                && scope.Values<string>().Distinct(StringComparer.Ordinal).Count() == scope.Count, at + "/counterpart_pages", "Supply 1..20 distinct exact counterpart pages.");
            double[] point = Point(item["position"], at + "/position");
            Node(point, [(string)item["direction"]!], "interruption_point", at);
            if (item["symbol"] is { } symbolName)
            {
                Require(symbolName.Type == JTokenType.String && ((string)symbolName!).Length is >= 1 and <= 64, at + "/symbol", "Supply a SPECIAL interruption point symbol name from eplan_symbol_search.");
                _interruptionSymbols[Key(point)] = (string)symbolName!;
            }
            if (item["symbol_variant"] is { } explicitVariant)
            {
                Require(explicitVariant.Type == JTokenType.Integer && int.TryParse(explicitVariant.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _), at + "/symbol_variant", "Supply an Int32 variant number.");
                _requestedVariants.Add(Key(point), ((int)explicitVariant, at + "/symbol_variant"));
            }
            var interruption = (JObject)item.DeepClone();
            // PDF redraw 2026-11-06: an interruption point's tag carries the higher-level function only, as in the sample (=GAA-2L1
            // on =GAA+A1 shows -2L1). On a page without one, =-L1 shows L1; a location in the tag makes EPLAN show the full name.
            string shared = (string)item["name"]!;
            // A121: a project without page structure (NFPA basic projects) shows a leading '=' as written, so none is added there.
            string function = scene.Page.Page.Contains('/') ? "=" + DrawingNames.HigherLevelFunctionOf(scene.Page.Page) : string.Empty;
            interruption["name"] = shared[0] is '=' or '+' ? shared : function + (shared[0] == '-' ? "" : "-") + shared;
            _interruptions.Add(Key(point), interruption);
        }

        private void ReadNode(JToken item, string at)
        {
            Members(item, at, "kind position directions", (string?)item["kind"] == "t_node" ? "variant source" : "");
            Require(item["directions"] is JArray d && d.All(v => v.Type == JTokenType.String), at + "/directions", "Supply a direction array.");
            double[] point = Point(item["position"], at + "/position");
            Node(point, item["directions"]!.Values<string>().Select(v => v!).ToArray(), (string?)item["kind"] ?? "", at);
            if (item["variant"] is { } variant)
            {
                Require(variant.Type == JTokenType.Integer && int.TryParse(variant.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _), at + "/variant", "Supply an Int32 variant number.");
                _requestedVariants.Add(Key(point), ((int)variant, at + "/variant"));
            }
            if ((string?)item["kind"] == "t_node" && DirectionalVariant(item["directions"]!.Values<string>().Select(v => v!).ToArray(), item["source"], item["variant"] is not null, at) is { } directional)
            { _directionalVariants.Add(Key(point), directional); }
        }

        private void ReadWire(JToken item, string at, int number)
        {
            Members(item, at, "kind from to points", "definition_point");
            Require(item["points"] is JArray { Count: >= 2 and <= 32 }, at + "/points", "Supply 2..32 orthogonal points.");
            JToken[] raw = [.. item["points"]!];
            double[][] points = raw.Select((p, i) =>
            {
                string pointer = at + "/points/" + i.ToString(CultureInfo.InvariantCulture);
                if ((i == 0 ? PinPoint(item["from"], p) : i == raw.Length - 1 ? PinPoint(item["to"], p) : null) is not { } pin)
                { return Point(p, pointer); }
                _requested.Add(new JObject { ["pointer"] = pointer, ["requested"] = p.DeepClone(), ["actual"] = new JArray(pin) });
                return pin;
            }).ToArray();
            Require(points.Select(Key).Distinct(StringComparer.Ordinal).Count() == points.Length, at + "/points", "Remove duplicate nodes.");
            for (int i = 1; i < points.Length; i++)
            { Require((points[i][0] == points[i - 1][0]) != (points[i][1] == points[i - 1][1]), at + "/points", "Use nonzero orthogonal segments."); }
            string from = Pin(item["from"], at + "/from"), to = Pin(item["to"], at + "/to");
            Require(scene.Pins[from].Location.SequenceEqual(points[0]) && scene.Pins[to].Location.SequenceEqual(points[points.Length - 1]), at, "Path endpoints must equal the located pins; pins are never moved.");
            Require(scene.Pins[from].Direction == Direction(points[0], points[1]) && scene.Pins[to].Direction == Direction(points[points.Length - 1], points[points.Length - 2]), at, "Path directions must face out of both pins.");
            if (item["definition_point"] is { } definition)
            { _definitions.Add(ConnectionDefinitionPoints.Plan(definition, at + "/definition_point", points, from, to, number, scene, _variants ??= reader.Read(scene.Project.Path, new() { Library = "SPECIAL" }), parts: ReadParts)); }
            _endpointPins.Add(from);
            _endpointPins.Add(to);
            _paths.Add(points);
        }

        private void AddBends()
        {
            foreach (var path in _paths)
            {
                for (int i = 1; i < path.Length - 1; i++)
                {
                    string[] directions = [Direction(path[i], path[i - 1]), Direction(path[i], path[i + 1])];
                    Require(directions[0] != directions[1], "/items", "Remove path reversals.");
                    if (!_nodes.ContainsKey(Key(path[i])) && directions[0] != Opposite(directions[1]))
                    { Node(path[i], directions, "corner", "/items"); }
                }
            }
        }
    }
}
