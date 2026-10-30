using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A52: complete-page prediction before either committed placement or generation.</summary>
public static class ConnectionCreatePlanner
{
    public sealed class Scene
    {
        public OpenProject Project { get; set; } = new();
        public PageDrawingGeometry Page { get; set; } = new();
        public string PageType { get; set; } = "Circuit";
        public bool Complete { get; set; }
        public Dictionary<string, SymbolConnectionPoint> Pins { get; set; } = [];
        public List<double[][]> Graphical { get; set; } = [];
        public List<ConnectionRecord> Logical { get; set; } = [];
        public Dictionary<string, double[]> SymbolBounds { get; set; } = [];
        public Func<CableDefinitionPlanner.Inventory>? ReadCables { get; set; }
    }

    public static JObject Plan(string json, Scene scene, ISymbolReader reader, string? expectedFingerprint = null)
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

    private static JObject Predict(string json, Scene scene, ISymbolReader reader, string? expectedFingerprint)
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
        Members(input, "", "project page items", "dry_run snap_to_grid response_format");
        FiniteNumbers(input, "");
        Require(input["project"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["project"]), "/project", "Specify an open project.");
        Require(input["page"]?.Type == JTokenType.String && (string?)input["page"] == scene.Page.Page, "/page", "Select the exact scene page.");
        Require((string?)input["project"] == scene.Project.Name || (string?)input["project"] == scene.Project.Path, "/project", "Re-read the specified project.");
        foreach (string name in new[] { "dry_run", "snap_to_grid" })
        { Require(input[name] is null || input[name]!.Type == JTokenType.Boolean, "/" + name, "Supply a boolean."); }
        Require(input["response_format"] is null || input["response_format"]!.Type == JTokenType.String && (string?)input["response_format"] is "concise" or "detailed", "/response_format", "Use concise or detailed.");
        Require(input["items"] is JArray { Count: >= 1 and <= 50 }, "/items", "Supply 1..50 items.");
        if (SceneGap(scene) is { } gap)
        { throw Missing("Read a complete Circuit page inventory with verified bounds and pin geometry: " + gap + "."); }
        bool snap = (bool?)input["snap_to_grid"] ?? true;
        double grid = scene.Page.GridSize;
        // Acceptance trial: library pins may be off grid (IEC_symbol X has pins at +2 and -0.75 mm), so only path
        // endpoints that equal their pin are exempt from the grid; bends and nodes stay on it.
        if (double.IsNaN(grid) || double.IsInfinity(grid) || grid <= 0)
        { throw Missing("Read a valid page grid."); }
        var paths = new List<double[][]>();
        var nodes = new Dictionary<string, (double[] Point, string[] Directions, string Kind)>(StringComparer.Ordinal);
        var requested = new JArray();
        var resolved = new JArray();
        var interruptions = new Dictionary<string, JObject>(StringComparer.Ordinal);
        string Key(double[] p) => FormattableString.Invariant($"{p[0]},{p[1]}");
        double[] Point(JToken? token, string at)
        {
            Require(token is JArray { Count: 2 } a && a.All(n => n.Type is JTokenType.Integer or JTokenType.Float && (double)n is >= 0 and <= 10000), at, "Supply [x_mm,y_mm] in 0..10000 mm.");
            double[] actual = token!.Select(n => DrawingCoordinates.Snap((double)n!, grid, snap)).ToArray();
            Require(actual.All(v => Math.Abs(v / grid - Math.Round(v / grid)) < 1e-8), at, "Connection points must lie on the page grid.");
            Require(DrawingCoordinates.Contains(scene.Page.PageSize, [[actual[0], actual[1], actual[0], actual[1]]]), at, "Keep the path inside Page.Size.");
            requested.Add(new JObject { ["pointer"] = at, ["requested"] = token!.DeepClone(), ["actual"] = new JArray(actual) });
            return actual;
        }
        // The exact, unsnapped location of a resolvable pin that the requested end point names; otherwise null.
        double[]? PinPoint(JToken? pin, JToken requested)
        {
            if (requested is not JArray { Count: 2 } r || r.Any(n => n.Type is not (JTokenType.Integer or JTokenType.Float)) || pin is not JObject
                || pin["locator"] is not JObject locator || pin["pin_index"]?.Type != JTokenType.Integer
                || locator["type"]?.Type != JTokenType.Integer || locator["oid"]?.Type != JTokenType.Integer)
            { return null; }
            string key = FormattableString.Invariant($"{(int?)locator["type"]}/{(long?)locator["oid"]}/{(int)pin["pin_index"]!}");
            return scene.Pins.TryGetValue(key, out SymbolConnectionPoint? located) && located.Location.Length == 2
                && Math.Abs((double)r[0] - located.Location[0]) < 1e-9 && Math.Abs((double)r[1] - located.Location[1]) < 1e-9 ? located.Location : null;
        }
        string Pin(JToken? token, string at)
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
        void Node(double[] point, string[] directions, string kind, string at)
        {
            Require(directions.Distinct(StringComparer.Ordinal).Count() == directions.Length && directions.All(d => d is "Up" or "Down" or "Left" or "Right"), at, "Supply distinct cardinal directions.");
            Require(kind switch { "corner" => directions.Length == 2 && directions[0] != Opposite(directions[1]), "t_node" => directions.Length == 3, "potential_point" or "interruption_point" => directions.Length == 1, _ => false }, at, "Directions must match the node kind.");
            Require(!nodes.ContainsKey(Key(point)), at, "Duplicate nodes are not allowed.");
            nodes.Add(Key(point), (point, directions, kind));
        }
        int index = 0;
        foreach (JToken item in (JArray)input["items"]!)
        {
            string at = "/items/" + (index++).ToString(CultureInfo.InvariantCulture);
            if (item is JObject && (string?)item["kind"] == "cable_definition")
            { continue; }
            Require(item is JObject && item["kind"]?.Type == JTokenType.String, at + "/kind", "Supply wire, corner, t_node, potential_point, interruption_point or cable_definition.");
            bool wire = (string?)item["kind"] == "wire";
            if ((string?)item["kind"] == "interruption_point")
            {
                Members(item, at, "kind name direction position counterpart_pages", "");
                Require(item["name"]?.Type == JTokenType.String && ((string)item["name"]!).Length is >= 1 and <= 200 && !string.IsNullOrWhiteSpace((string?)item["name"])
                    && !((string)item["name"]!).Any(char.IsControl), at + "/name", "Supply the exact shared full business name (1..200 characters).");
                Require(item["direction"]?.Type == JTokenType.String, at + "/direction", "Supply Up, Down, Left or Right.");
                Require(item["counterpart_pages"] is JArray { Count: >= 1 and <= 20 } scope && scope.All(p => p.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)p))
                    && scope.Values<string>().Distinct(StringComparer.Ordinal).Count() == scope.Count, at + "/counterpart_pages", "Supply 1..20 distinct exact counterpart pages.");
                double[] point = Point(item["position"], at + "/position");
                Node(point, [(string)item["direction"]!], "interruption_point", at);
                interruptions.Add(Key(point), (JObject)item.DeepClone());
                continue;
            }
            Members(item, at, wire ? "kind from to points" : "kind position directions", "");
            if (!wire)
            {
                Require(item["directions"] is JArray d && d.All(v => v.Type == JTokenType.String), at + "/directions", "Supply a direction array.");
                Node(Point(item["position"], at + "/position"), item["directions"]!.Values<string>().Select(v => v!).ToArray(), (string?)item["kind"] ?? "", at);
                continue;
            }
            Require(item["points"] is JArray { Count: >= 2 and <= 32 }, at + "/points", "Supply 2..32 orthogonal points.");
            JToken[] raw = [.. item["points"]!];
            double[][] points = raw.Select((p, i) =>
            {
                string pointer = at + "/points/" + i.ToString(CultureInfo.InvariantCulture);
                if ((i == 0 ? PinPoint(item["from"], p) : i == raw.Length - 1 ? PinPoint(item["to"], p) : null) is not { } pin)
                { return Point(p, pointer); }
                requested.Add(new JObject { ["pointer"] = pointer, ["requested"] = p.DeepClone(), ["actual"] = new JArray(pin) });
                return pin;
            }).ToArray();
            Require(points.Select(Key).Distinct(StringComparer.Ordinal).Count() == points.Length, at + "/points", "Remove duplicate nodes.");
            for (int i = 1; i < points.Length; i++)
            { Require((points[i][0] == points[i - 1][0]) != (points[i][1] == points[i - 1][1]), at + "/points", "Use nonzero orthogonal segments."); }
            string from = Pin(item["from"], at + "/from"), to = Pin(item["to"], at + "/to");
            Require(scene.Pins[from].Location.SequenceEqual(points[0]) && scene.Pins[to].Location.SequenceEqual(points[points.Length - 1]), at, "Path endpoints must equal the located pins; pins are never moved.");
            Require(scene.Pins[from].Direction == Direction(points[0], points[1]) && scene.Pins[to].Direction == Direction(points[points.Length - 1], points[points.Length - 2]), at, "Path directions must face out of both pins.");
            paths.Add(points);
        }
        foreach (var path in paths)
        {
            for (int i = 1; i < path.Length - 1; i++)
            {
                string[] directions = [Direction(path[i], path[i - 1]), Direction(path[i], path[i + 1])];
                Require(directions[0] != directions[1], "/items", "Remove path reversals.");
                if (!nodes.ContainsKey(Key(path[i])) && directions[0] != Opposite(directions[1]))
                { Node(path[i], directions, "corner", "/items"); }
            }
        }
        List<SymbolSearchItem> variants = reader.Read(scene.Project.Path, new() { Library = "SPECIAL" });
        foreach (var node in nodes.Values)
        {
            SymbolSearchItem[] matches = variants.Where(s => s.Library == "SPECIAL" && (node.Kind switch
            {
                "corner" => s.Name == "CO" && s.Type == "Routing",
                "t_node" => s.Type == (s.Name switch { "TLRO" => "TNodeUp", "TLRU" => "TNodeDown", "TOUL" => "TNodeLeft", "TOUR" => "TNodeRight", _ => "" }),
                "interruption_point" => s.Name == "BPF" && s.Type == "InterruptionPoint",
                _ => s.Name == "PCP" && s.Type == "PotentialTerminal",
            }) && s.ConnectionPoints.Count == node.Directions.Length && s.ConnectionPoints.All(p => p.Location.Length == 2
                && p.Location.All(v => !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) <= 2 * grid)
                && (node.Kind is "potential_point" or "interruption_point" || p.Location.All(v => v == 0)))
                && s.ConnectionPoints.Select(p => p.Direction).OrderBy(d => d, StringComparer.Ordinal).SequenceEqual(node.Directions.OrderBy(d => d, StringComparer.Ordinal))).OrderBy(s => s.Variant).ToArray();
            // A52/S40: PCP has one offset pin and mirrored variants sharing its direction.
            if (node.Kind is "potential_point" or "interruption_point" && matches.Length > 0)
            { matches = matches.Where(s => s.Variant == matches[0].Variant).ToArray(); }
            SymbolSearchItem symbol = matches.Length == 1 ? matches[0] : throw Missing("Select a verified SPECIAL variant with the requested cardinal pins using eplan_symbol_search.");
            double[] offset = symbol.ConnectionPoints[0].Location;
            double[] position = [node.Point[0] - offset[0], node.Point[1] - offset[1]];
            string key = FormattableString.Invariant($"{symbol.Name}/{symbol.Variant}");
            double[] bounds = scene.SymbolBounds.TryGetValue(key, out double[]? found) ? found : throw Missing("Read the selected variant's complete outline before planning placement.");
            double[] outline = [bounds[0] + position[0], bounds[1] + position[1], bounds[2] + position[0], bounds[3] + position[1]];
            Require(position.All(v => v is >= 0 and <= 10000) && DrawingCoordinates.Contains(scene.Page.PageSize, [outline, [position[0], position[1], position[0], position[1]]]), "/items", "Keep the full symbol outline inside Page.Size and insertion coordinates in 0..10000 mm.");
            Require(!scene.Page.Placements.Any(p => Overlaps(outline, p.Bounds!)) && !resolved.Any(p => Overlaps(outline, p["bounds"]!.Values<double>().ToArray())), "/items", "A symbol outline conflicts with another placement.");
            // Functional discovery metadata does not widen the existing routing-symbol contract.
            resolved.Add(new JObject { ["position"] = new JArray(position), ["symbol"] = BridgeJson.FromObject(new { symbol.Library, symbol.Name, symbol.Variant, symbol.Type, symbol.Kind, symbol.ConnectionPoints }), ["bounds"] = new JArray(outline) });
            if (interruptions.TryGetValue(Key(node.Point), out JObject? interruption))
            { resolved.Last!["interruption"] = interruption; }
        }
        var segments = paths.SelectMany(p => p.Zip(p.Skip(1), (a, b) => new[] { a, b })).ToList();
        var ports = scene.Pins.Values.Select(p => (Point: p.Location, p.Direction)).Concat(nodes.Values.SelectMany(n => n.Directions.Select(d => (n.Point, Direction: d)))).ToArray();
        foreach (var node in nodes.Values)
        {
            foreach (string direction in node.Directions)
            {
                double[][] candidates = ports.Where(p => p.Direction == Opposite(direction) && !p.Point.SequenceEqual(node.Point) && Direction(node.Point, p.Point) == direction
                    && (p.Point[0] == node.Point[0] || p.Point[1] == node.Point[1])).Select(p => p.Point).OrderBy(p => Distance(p, node.Point)).ToArray();
                if (candidates.Length > 0 && !segments.Any(s => On(node.Point, s) && s.Any(p => !p.SequenceEqual(node.Point) && Direction(node.Point, p) == direction)))
                { segments.Add([node.Point, candidates[0]]); }
            }
        }
        segments = segments.SelectMany(s =>
        {
            double[][] points = s.Concat(nodes.Values.Select(n => n.Point).Where(p => On(p, s))).GroupBy(Key, StringComparer.Ordinal).Select(g => g.First()).OrderBy(p => Distance(s[0], p)).ToArray();
            return points.Zip(points.Skip(1), (a, b) => new[] { a, b });
        }).ToList();
        for (int i = 0; i < segments.Count; i++)
        {
            double[][] s = segments[i];
            Require(!scene.Graphical.Any(e => Intersects(s, e)), "/items", "The path conflicts with existing autoconnect lines; plan an explicit replacement separately.");
            if (scene.Page.Placements.FirstOrDefault(p => CrossesPlacement(s, p, scene.Pins)) is { } crossed)
            {
                Require(false, "/items", FormattableString.Invariant(
                    $"The path {FormatPoint(s[0])} -> {FormatPoint(s[1])} crosses the outline of {crossed.Kind} {crossed.Locator.Type}/{crossed.Locator.Oid} [{string.Join(",", crossed.Bounds!.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))}]."));
            }
            Require(!scene.Pins.Values.Any(p => On(p.Location, s) && !s.Any(e => e.SequenceEqual(p.Location))), "/items", "A different pin intercepts this path; select explicit endpoints.");
            foreach (var node in nodes.Values.Where(n => On(n.Point, s)))
            { Require(s.Where(p => !p.SequenceEqual(node.Point)).All(p => node.Directions.Contains(Direction(node.Point, p), StringComparer.Ordinal)), "/items", "A node's directions do not support this path."); }
            for (int j = 0; j < i; j++)
            {
                if (!Intersects(s, segments[j]))
                { continue; }
                double[][] ends = s.Concat(segments[j]).Concat(nodes.Values.Select(n => n.Point)).Where(p => On(p, s) && On(p, segments[j])).GroupBy(Key, StringComparer.Ordinal).Select(g => g.First()).ToArray();
                Require(ends.Length == 1 && (nodes.TryGetValue(Key(ends[0]), out var junction) && junction.Kind == "t_node" || s.Any(p => p.SequenceEqual(ends[0])) && segments[j].Any(p => p.SequenceEqual(ends[0]))), "/items", "Reject ambiguous crossings or overlapping segments; declare a T-node explicitly.");
            }
        }
        var logical = new JArray();
        var vertices = ports.Select(p => p.Point).Concat(segments.SelectMany(s => s)).GroupBy(Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var groups = vertices.Keys.ToDictionary(k => k, k => new HashSet<string>(StringComparer.Ordinal) { k }, StringComparer.Ordinal);
        foreach (double[][] segment in segments)
        {
            var group = new HashSet<string>(vertices.Where(p => On(p.Value, segment)).SelectMany(p => groups[p.Key]), StringComparer.Ordinal);
            foreach (string key in group)
            { groups[key] = group; }
        }
        foreach (HashSet<string> connected in groups.Values.Distinct())
        {
            string[] ends = scene.Pins.Where(p => connected.Contains(Key(p.Value.Location))).Select(p => p.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            bool potential = nodes.Values.Any(n => n.Kind == "potential_point" && connected.Contains(Key(n.Point)));
            if (ends.Length > 1 || potential && ends.Length > 0)
            { logical.Add(new JObject { ["pins"] = new JArray(ends), ["potentialStartIsNull"] = potential }); }
        }
        var result = new JObject
        {
            ["project"] = scene.Project.Name,
            ["page"] = scene.Page.Page,
            ["execution"] = ExecutionState.NotStarted,
            ["dryRun"] = true,
            ["coordinates"] = requested,
            ["symbols"] = resolved,
            ["graphicalAutoconnect"] = JArray.FromObject(segments),
            ["logicalAfterGenerate"] = logical,
            ["gridSize"] = grid,
            ["pageSize"] = new JArray(scene.Page.PageSize!),
            ["coverage"] = "complete_page_predicted_endpoints",
            ["logicalDuringPlacement"] = 0,
            ["generation"] = "One call commits placement then generation: 2 undo steps. Generation failure undoes placement automatically.",
            ["undoSteps"] = 2
        };
        if (interruptions.Count > 0)
        { InterruptionPointPlanning.Plan(result, scene, reader as IConnectionCreator ?? throw Missing("Read explicit counterpart scope before planning interruption points.")); }
        string cableFingerprint = CableDefinitionPlanner.Add(input, scene, result, Point);
        string fingerprint = Fingerprint.Of([input.ToString(Formatting.None), .. (cableFingerprint == "" ? Array.Empty<string>() : new[] { cableFingerprint }), BridgeJson.FromObject(new { scene.Project, PageProject = scene.Page.Project, scene.Page.Page, scene.Page.GridSize, scene.Page.PageSize, scene.PageType, scene.Complete }).ToString(Formatting.None), resolved.ToString(Formatting.None),
            .. scene.Page.Placements.Select(p => "placement:" + BridgeJson.FromObject(p).ToString(Formatting.None)), .. scene.Pins.Select(p => "pin:" + p.Key + BridgeJson.FromObject(p.Value).ToString(Formatting.None)),
            .. scene.Graphical.Select(p => "graphical:" + BridgeJson.FromObject(p).ToString(Formatting.None)), .. scene.Logical.Select(p => "logical:" + BridgeJson.FromObject(p).ToString(Formatting.None)), .. scene.SymbolBounds.Select(p => "outline:" + p.Key + BridgeJson.FromObject(p.Value).ToString(Formatting.None)), .. result["interruptionScope"] is { } evidence ? new[] { evidence.ToString(Formatting.None) } : []]);
        result["fingerprint"] = fingerprint;
        return expectedFingerprint is null || expectedFingerprint == fingerprint ? result : throw new BridgeErrorException(ErrorReason.PlanStale, "Page, pins or library geometry changed; create a new dry-run plan.");
    }

    private static string Opposite(string d) => d switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", "Right" => "Left", _ => "" };
    private static bool CrossesPlacement(double[][] segment, DrawingPlacement placement, Dictionary<string, SymbolConnectionPoint> pins)
    {
        double[] bounds = placement.Bounds!;
        double[][] diagonal = [[bounds[0], bounds[1]], [bounds[2], bounds[3]]];
        if (!Overlaps(Box(segment), bounds) && !((bounds[0] == bounds[2] || bounds[1] == bounds[3]) && Intersects(segment, diagonal)))
        { return false; }
        string owner = FormattableString.Invariant($"{placement.Locator.Type}/{placement.Locator.Oid}/");
        // Only the terminal approach to this placement's own pin may enter its outline.
        return !pins.Any(p => p.Key.StartsWith(owner, StringComparison.Ordinal) && On(p.Value.Location, diagonal)
            && segment.Any(end => end.SequenceEqual(p.Value.Location) && segment.Any(other => !On(other, diagonal)
                && Direction(end, other) == p.Value.Direction)));
    }
    private static string Direction(double[] a, double[] b) => a[0] == b[0] ? b[1] > a[1] ? "Up" : "Down" : b[0] > a[0] ? "Right" : "Left";
    private static double Distance(double[] a, double[] b) => Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]);
    private static double[] Box(double[][] s) => [Math.Min(s[0][0], s[1][0]), Math.Min(s[0][1], s[1][1]), Math.Max(s[0][0], s[1][0]), Math.Max(s[0][1], s[1][1])];
    private static bool On(double[] p, double[][] s) => p[0] >= Box(s)[0] && p[0] <= Box(s)[2] && p[1] >= Box(s)[1] && p[1] <= Box(s)[3];
    private static bool Overlaps(double[] a, double[] b) => a[0] < b[2] && a[2] > b[0] && a[1] < b[3] && a[3] > b[1];
    internal static bool Intersects(double[][] a, double[][] b) => Box(a)[0] <= Box(b)[2] && Box(a)[2] >= Box(b)[0] && Box(a)[1] <= Box(b)[3] && Box(a)[3] >= Box(b)[1];
    internal static void Require(bool valid, string at, string message) { if (!valid) { throw WriteArguments.Invalid([at + ": " + message]); } }
    // Names the first failed scene check so the caller can tell which page object blocks drawing.
    private static string? SceneGap(Scene scene)
    {
        if (!scene.Complete)
        { return "a page placement has no drawing record"; }
        if (scene.PageType != "Circuit")
        { return "page type " + scene.PageType; }
        if (scene.Page.Project != scene.Project.Path || scene.Page.PageSize is null)
        { return "page geometry is unavailable"; }
        if (scene.Page.Placements.FirstOrDefault(p => !DrawingCoordinates.ValidBounds(p.Bounds)) is { } placement)
        { return FormattableString.Invariant($"invalid bounds of {placement.Kind} {placement.Locator.Type}/{placement.Locator.Oid}"); }
        if (scene.SymbolBounds.FirstOrDefault(b => !DrawingCoordinates.ValidBounds(b.Value)) is { Key: not null } symbol)
        { return "invalid outline of symbol " + symbol.Key; }
        if (scene.Pins.Count > 10000)
        { return "more than 10000 pins"; }
        if (scene.Pins.FirstOrDefault(p => p.Value.Location.Length != 2 || p.Value.Location.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 10000) || Opposite(p.Value.Direction) == ""
            || !DrawingCoordinates.Contains(scene.Page.PageSize, [[p.Value.Location[0], p.Value.Location[1], p.Value.Location[0], p.Value.Location[1]]])) is { Key: not null } pin)
        { return "pin " + pin.Key + " at " + FormatPoint(pin.Value.Location) + " direction " + pin.Value.Direction + " is outside the page or undirected"; }
        if (scene.Pins.GroupBy(p => FormatPoint(p.Value.Location), StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } shared)
        { return "pins " + string.Join(", ", shared.Select(p => p.Key)) + " share " + shared.Key; }
        return null;
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
