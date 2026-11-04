using System.Globalization;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

internal sealed partial class SmallProjectRedrawDriver
{
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
            JObject? owner = objects.FirstOrDefault(o => Id(o) == group.Key && Type(o) is not ("PropertyPlacement" or "ContextPropertyPlacement"));
            JObject[] parents = owner is null ? [] : actual.Where(a => a["parent"]?.Type is null or JTokenType.Null && DrawingMatches(owner, a)).ToArray();
            if (owner is null || parents.Length != 1)
            {
                foreach (JObject item in group)
                { report.Warning(page, Type(item) + " " + Id(item) + ": source or target parent is absent or ambiguous; no property move was guessed."); }
                continue;
            }
            JObject parent = parents[0];
            foreach (JObject item in group)
            {
                await ObserveAsync(page, item, () =>
                {
                    string? propertyId = item["propertyId"]?.ToString();
                    JObject[] matches = actual.Where(a => JToken.DeepEquals(a["parent"], parent["locator"])
                        && Text(a["properties"]?["property_id"]) == propertyId).ToArray();
                    // Exported propertyIndex is AnyPropertyId.Index, not the target collection slot.
                    if (matches.Length != 1 || group.Count(p => p["propertyId"]?.ToString() == propertyId) != 1
                        || !int.TryParse(Text(matches[0]["properties"]?["property_index"]), NumberStyles.None, CultureInfo.InvariantCulture, out int targetSlot))
                    { throw new InvalidDataException("Displayed property ID is absent or ambiguous, or the target collection slot is unreadable; no selector was guessed."); }
                    JObject target = matches[0];
                    JObject current = target["properties"] as JObject ?? new JObject();
                    double[] offset = Point(item["location"]), origin = Point(owner["location"]);
                    double[] desired = [origin[0] + offset[0], origin[1] + offset[1]];
                    int angle = PropertyAngle(item["angle"]);
                    double[] position = Point(target["location"]);
                    bool different = Math.Abs(desired[0] - position[0]) > 0.01 || Math.Abs(desired[1] - position[1]) > 0.01
                        || !SameStyleNumber(angle, current["angle_deg"]);
                    // Selectors, coordinates and angle are required even for a style-only change.
                    var move = new JObject
                    {
                        ["parent"] = parent["locator"]!.DeepClone(),
                        ["property_id"] = item["propertyId"]!.DeepClone(),
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
