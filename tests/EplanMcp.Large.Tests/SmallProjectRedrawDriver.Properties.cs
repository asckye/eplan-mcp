using System.Globalization;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

internal sealed partial class SmallProjectRedrawDriver
{
    private static readonly string[] _propertyParentIdentityFields = ["type", "oid", "page", "project"];
    private readonly Dictionary<string, (JObject[] Items, bool Complete)> _drawingCache = new(StringComparer.Ordinal);

    private async Task MovePropertiesAsync(string page, JObject[] objects)
    {
        JObject[] properties = objects.Where(o => Type(o) is "ContextPropertyPlacement" or "PropertyPlacement").ToArray();
        if (properties.Length == 0)
        { return; }
        // One complete snapshot per phase, shared by all owners. A write invalidates it for the next phase.
        JObject[] actual = await ReadDrawingsAsync(page, properties[0]);
        var moves = new List<(JObject Source, JObject Move)>();
        foreach (IGrouping<string, JObject> group in properties.GroupBy(p => p["parentOid"]?.ToString() ?? "", StringComparer.Ordinal))
        {
            JObject[] owners = objects.Where(o => Id(o) == group.Key && Type(o) is not ("PropertyPlacement" or "ContextPropertyPlacement")).ToArray();
            JObject? owner = owners.Length == 1 ? owners[0] : null;
            JObject[] parents = owner is null ? [] : PropertyParents(page, owner, actual);
            if (owner is null || parents.Length != 1)
            {
                foreach (JObject item in group)
                {
                    report.Warning(page, Type(item) + " " + Id(item) + ": property " + item["propertyId"] + " index " + item["propertyIndex"]
                    + " source or target parent is absent or ambiguous (source candidates=" + owners.Length + ", target candidates="
                    + parents.Length + ", source owner=" + owner?["name"] + "); no property move was guessed.");
                }
                continue;
            }
            JObject parent = parents[0];
            var available = actual.Where(a => Text(a["kind"]) is "PropertyPlacement" or "ContextPropertyPlacement"
                && SameParent(a["parent"], parent["locator"])).ToList();
            foreach (JObject item in group)
            {
                await ObserveAsync(page, item, () =>
                {
                    double[] offset = Point(item["location"]), origin = Point(owner["location"]);
                    double[] desired = [origin[0] + offset[0], origin[1] + offset[1]];
                    (int? propertyId, int? propertyIndex, string? inferred) = PropertyIdentity(item, owner, available, desired);
                    if (propertyId is null)
                    { throw new InvalidDataException("Displayed property ID cannot be inferred from exported content and position; no selector was guessed."); }
                    if (inferred is not null)
                    { report.Note(page, Type(item), Id(item), "Displayed property " + propertyId + " index " + propertyIndex + " inferred from " + inferred + "."); }
                    JObject[] candidates = available.Where(a => DisplayedPropertyId(a) == propertyId).ToArray();
                    JObject[] unreadable = available.Where(a => DisplayedPropertyId(a) is null).ToArray();
                    JObject? target = MatchProperty(item, owner, propertyId.Value, propertyIndex, group.ToArray(), candidates, desired)
                        ?? MatchProperty(item, owner, propertyId.Value, propertyIndex, group.ToArray(), unreadable, desired, allowSingle: false);
                    if (target is null)
                    {
                        if (_lastDrawingReadComplete && candidates.Length == 0 && unreadable.Length == 0)
                        {
                            report.MissingPropertyPlacement(page, Type(item), Id(item), propertyId, propertyIndex,
                            "Target owner exists, but its default property placements do not contain this displayed property; no tool can create it.");
                        }
                        else
                        {
                            report.Warning(page, Type(item) + " " + Id(item) + ": displayed property " + propertyId + " index " + propertyIndex
                            + " has absent or ambiguous target content/position; the target collection slot is not the native property index, so no selector was guessed.");
                        }
                        return Task.CompletedTask;
                    }
                    // The MCP property_index is the collection slot, not exported AnyPropertyId.Index.
                    if (Integer(target["properties"]?["property_index"]) is not int targetSlot)
                    { throw new InvalidDataException("Target property collection slot is unreadable; no selector was guessed."); }
                    available.Remove(target);
                    JObject current = target["properties"] as JObject ?? new JObject();
                    int angle = PropertyAngle(item["angle"]);
                    double[] position = Point(target["location"]);
                    bool different = Math.Abs(desired[0] - position[0]) > 0.01 || Math.Abs(desired[1] - position[1]) > 0.01
                        || !SameStyleNumber(angle, current["angle_deg"]);
                    // Selectors, coordinates and angle are required even for a style-only change.
                    var move = new JObject
                    {
                        ["parent"] = parent["locator"]!.DeepClone(),
                        ["property_id"] = propertyId.Value,
                        ["property_index"] = targetSlot,
                        ["x_mm"] = desired[0],
                        ["y_mm"] = desired[1],
                        ["angle_deg"] = angle,
                    };
                    foreach ((string from, string to, string readback) in new[]
                    {
                        ("alignment", "alignment", "alignment"), ("height", "height_mm", "height_raw_mm"),
                        ("isVisible", "visible", "visible"), ("layer", "layer", "layer"),
                    })
                    {
                        if (item[from] is { Type: not JTokenType.Null } value && !SameStyleValue(value, current[readback]))
                        { move[to] = value.DeepClone(); different = true; }
                    }
                    var pen = new JObject();
                    foreach ((string from, string to) in new[]
                    {
                        ("color", "color"), ("width", "width_mm"), ("style", "style"),
                        ("patternLength", "pattern_length"), ("lineEndType", "line_end"),
                    })
                    {
                        if (item["pen"]?[from] is { Type: not JTokenType.Null } value && !SameStyleValue(value, current["pen_" + to]))
                        { pen[to] = value.DeepClone(); }
                    }
                    if (pen.Count > 0)
                    { move["pen"] = pen; different = true; }
                    if (different)
                    { moves.Add((item, move)); }
                    return Task.CompletedTask;
                });
            }
        }
        foreach (var batch in moves.Chunk(50))
        {
            await InvokeAsync(page, "eplan_placement_update", new JObject
            {
                ["page"] = page,
                ["snap_to_grid"] = false,
                ["property_moves"] = new JArray(batch.Select(m => m.Move)),
            }, batch.Select(m => m.Source).ToArray());
        }
    }

    private JObject[] PropertyParents(string page, JObject source, JObject[] actual)
    {
        var candidates = actual.Where(a => a["parent"]?.Type is null or JTokenType.Null
            && Type(source) == Text(a["kind"]) && ClosePoint(source["location"], a["location"])
            && (Text(a["locator"]?["page"]) is not string targetPage || targetPage == page)).ToArray();
        string? name = Text(source["name"]);
        JObject[] exact = candidates.Where(a => string.Equals(name ?? "", Text(a["properties"]?["name"]) ?? "", StringComparison.Ordinal)).ToArray();
        if (exact.Length > 0 || name is null)
        { return exact; }
        string mapped = _actualNames.GetValueOrDefault(name, name);
        // Device placement can return the owner's tag for a terminal. Preserve the terminal designation.
        if (name.LastIndexOf(':') is int colon && colon >= 0 && !mapped.Contains(':', StringComparison.Ordinal))
        { mapped += name[colon..]; }
        return candidates.Where(a => Text(a["properties"]?["name"]) == mapped).ToArray();
    }

    private static bool SameParent(JToken? first, JToken? second) => first is JObject && second is JObject
        && _propertyParentIdentityFields.All(field => JToken.DeepEquals(first[field], second[field]));

    private static int? Integer(JToken? token) => int.TryParse(token?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static int? DisplayedPropertyId(JObject target)
    {
        int? id = Integer(target["properties"]?["property_id"]) ?? Integer(target["properties"]?["displayed_property_id"]);
        return id > 0 ? id : null;
    }

    private static (int? Id, int? Index, string? Inferred) PropertyIdentity(JObject item, JObject owner, List<JObject> targets, double[] desired)
    {
        int? id = Integer(item["propertyId"]), index = Integer(item["propertyIndex"]);
        if (id > 0)
        { return (id, index, null); }
        var values = Objects(owner["properties"]).Where(p => Integer(p["id"]) > 0
            && (index is null || Integer(p["index"]) == index) && ContentsOverlap(item["content"], p["value"]))
            .Select(p => (Id: Integer(p["id"]), Index: Integer(p["index"]))).Distinct().ToArray();
        if (values.Length == 1)
        { return (values[0].Id, index ?? values[0].Index, "the owner's exported property value"); }
        JObject[] positioned = targets.Where(t => ClosePoint(new JArray(desired), t["location"]) && DisplayedPropertyId(t) is not null).ToArray();
        JObject[] content = positioned.Where(t => ContentMatches(item["content"], t)).ToArray();
        JObject[] evidence = content.Length > 0 ? content : positioned;
        if (evidence.Length == 1)
        { return (DisplayedPropertyId(evidence[0]), index, "the target's unique absolute position and available content"); }
        return (null, index, null);
    }

    private static JObject? MatchProperty(JObject item, JObject owner, int id, int? index, JObject[] siblings,
        JObject[] candidates, double[] desired, bool allowSingle = true)
    {
        if (candidates.Length == 0)
        { return null; }
        JToken? indexedValue = Objects(owner["properties"]).FirstOrDefault(p => Integer(p["id"]) == id && Integer(p["index"]) == index)?["value"];
        JObject[] content = candidates.Where(t => ContentMatches(item["content"], t) || ContentMatches(indexedValue, t)).ToArray();
        if (content.Length == 1)
        { return content[0]; }
        bool repeatedId = siblings.Count(s => Integer(s["propertyId"]) == id) > 1;
        JObject[] located = (content.Length > 0 ? content : candidates).Where(t => ClosePoint(new JArray(desired), t["location"])
            && (!repeatedId || content.Length > 0 || !HasContent(item["content"]) || !HasContent(t["properties"]?["display"]))).ToArray();
        if (located.Length == 1)
        { return located[0]; }
        // A single readable ID on each side identifies a pair even when its style needs moving.
        return allowSingle && candidates.Length == 1 && siblings.Count(s => Integer(s["propertyId"]) == id) == 1 ? candidates[0] : null;
    }

    private static bool ContentMatches(JToken? content, JObject target)
    {
        if (ContentsOverlap(content, target["properties"]?["display"]))
        { return true; }
        string? text = Text(target["properties"]?["text"]);
        return content is JObject languages && languages.Properties().Any(p => !string.IsNullOrEmpty(Text(p.Value))
            && text == p.Name + "@" + Text(p.Value) + ";");
    }

    private static bool HasContent(JToken? value) => value is JObject languages
        ? languages.Properties().Any(p => !string.IsNullOrEmpty(Text(p.Value))) : !string.IsNullOrEmpty(Text(value));

    private static bool ContentsOverlap(JToken? left, JToken? right)
    {
        static IEnumerable<string> Values(JToken? value) => value is JObject languages
            ? languages.Properties().Select(p => p.Value.ToString()).Where(v => !string.IsNullOrEmpty(v))
            : value is JValue && !string.IsNullOrEmpty(value.ToString()) ? [value.ToString()] : [];
        return Values(left).Intersect(Values(right), StringComparer.Ordinal).Any();
    }

    private static int PropertyAngle(JToken? value)
    {
        double raw = RequiredNumber(value);
        if (raw == -16002)
        { return -16002; }
        double angle = raw * 180 / Math.PI;
        int normalized = ((int)Math.Round(angle) % 360 + 360) % 360;
        if (Math.Abs(angle - Math.Round(angle)) > 0.000001 || normalized is not (0 or 90 or 180 or 270))
        { throw new InvalidDataException("Property rotation cannot be represented by the tool's right-angle contract."); }
        return normalized;
    }

    private static bool SameStyleValue(JToken source, JToken? target) => source.Type switch
    {
        JTokenType.Integer or JTokenType.Float => SameStyleNumber((double)source, target),
        JTokenType.Boolean => bool.TryParse(target?.ToString(), out bool value) && value == (bool)source,
        _ => source.ToString() == target?.ToString(),
    };

    private static bool SameStyleNumber(double source, JToken? target) => (source == -16002 && Text(target) == "from_layer")
        || (double.TryParse(target?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            && Math.Abs(source - number) <= 0.000001);

    private async Task<JObject[]> ReadDrawingsAsync(string page, JObject source)
    {
        if (_drawingCache.TryGetValue(page, out var cached))
        { _lastDrawingReadComplete = cached.Complete; return cached.Items; }
        _lastDrawingReadComplete = true;
        var result = new List<JObject>();
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var args = new JObject { ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["language"] = "??_??" };
            if (cursor is not null)
            { args["drawing_cursor"] = cursor; }
            JObject? response = await InvokeAsync(page, "eplan_page_get", args, [source], write: false);
            if (response is null)
            { _lastDrawingReadComplete = false; break; }
            JObject? drawings = response["drawings"] as JObject;
            if (drawings is null)
            {
                report.Warning(page, "MCP page read returned no drawing evidence.");
                _lastDrawingReadComplete = false;
                break;
            }
            result.AddRange(Objects(drawings["items"]));
            cursor = Text(drawings["nextCursor"]);
            if ((bool?)drawings["truncated"] == true && cursor is null)
            { _lastDrawingReadComplete = false; report.Warning(page, "MCP drawing evidence was truncated without a continuation cursor."); }
            if (cursor is not null && !seen.Add(cursor))
            {
                report.Warning(page, "MCP drawing cursor repeated; remaining evidence is unreadable.");
                _lastDrawingReadComplete = false;
                break;
            }
        } while (cursor is not null);
        JObject[] items = result.ToArray();
        _drawingCache[page] = (items, _lastDrawingReadComplete);
        return items;
    }
}
