using System.Globalization;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

internal sealed partial class SmallProjectRedrawDriver
{
    private static bool IsGraphic(JObject item) => Type(item) is "Line" or "Rectangle" or "PolyLine" or "Arc" or "Text" or "PathText" or "Image";

    private async Task DrawGraphicsAsync(string page, JObject[] objects)
    {
        var mapped = new List<(JObject Source, JObject Item)>();
        foreach (JObject item in objects.Where(IsGraphic).Where(item => !_covered.Contains(Key(page, item))))
        {
            if (Type(item) is "Text" or "PathText" && IsEmptyText(item["content"]))
            {
                report.Note(page, Type(item), Id(item), "Empty text content; no placement_create call was made.");
                continue;
            }
            await ObserveAsync(page, item, () =>
            {
                JObject graphic = Graphic(page, item);
                mapped.Add((item, graphic));
                return Task.CompletedTask;
            });
        }
        foreach ((JObject Source, JObject Item)[] batch in mapped.Chunk(50))
        {
            await InvokeItemsAsync(page, "eplan_placement_create", new JObject
            {
                ["page"] = page,
                ["snap_to_grid"] = false,
                ["items"] = new JArray(batch.Select(item => item.Item)),
            }, batch.Select(item => item.Source).ToArray());
        }
    }

    private JObject Graphic(string page, JObject source)
    {
        var item = new JObject();
        switch (Type(source))
        {
            case "Line":
                item["kind"] = "line";
                item["points"] = new JArray(new JArray(Point(source["start"])), new JArray(Point(source["end"])));
                break;
            case "Rectangle":
                item["kind"] = "rectangle";
                item["corners"] = new JArray(new JArray(Point(source["lowerLeft"])), new JArray(Point(source["upperRight"])));
                NoteRotation(page, source);
                break;
            case "PolyLine":
                item["kind"] = "polyline";
                item["points"] = new JArray((source["points"] as JArray ?? throw new InvalidDataException("Polyline vertices are unreadable."))
                    .Select(point => new JArray(Point(point))));
                if ((bool?)source["closed"] == true || (source["bulges"] as JArray)?.Any(value => Number(value) != 0) == true)
                { report.Note(page, Type(source), Id(source), "placement_create supports open straight polylines only; exported closed/bulged geometry remains a reported difference."); }
                break;
            case "Arc":
                item["center"] = new JArray(Point(source["center"]));
                if (Text(source["kind"]) == "Ellipse")
                {
                    item["kind"] = "ellipse";
                    item["radius_x_mm"] = source["firstRadius"]?.DeepClone();
                    item["radius_y_mm"] = source["secondRadius"]?.DeepClone();
                    item["angle"] = RequiredNumber(source["ellipseAngle"]) * 180 / Math.PI;
                }
                else
                {
                    item["kind"] = "arc";
                    item["radius_mm"] = source["radius"]?.DeepClone();
                    // Arc.StartAngle/EndAngle use radians; a native circle conventionally exports 0/0.
                    bool circle = Text(source["kind"]) == "Circle";
                    item["start_angle"] = circle ? 0 : RequiredNumber(source["startAngle"]) * 180 / Math.PI;
                    item["end_angle"] = circle ? 360 : RequiredNumber(source["endAngle"]) * 180 / Math.PI;
                }
                if (Text(source["kind"]) is not ("Circle" or "Arc" or "Ellipse") || (bool?)source["sector"] == true)
                { report.Note(page, Type(source), Id(source), "Exported arc kind or sector semantics have no placement_create parameter."); }
                break;
            case "Text":
            case "PathText":
                item["kind"] = Type(source) == "PathText" ? "path_text" : "text";
                item["position"] = new JArray(Point(source["location"]));
                item["contents"] = source["content"]?.DeepClone();
                if (Type(source) != "PathText" || Number(source["height"]) != -16002)
                { item["height_mm"] = source["height"]?.DeepClone(); }
                NoteRotation(page, source);
                if (Text(source["alignment"]) is string alignment)
                { report.Note(page, Type(source), Id(source), "Exported text alignment " + alignment + " has no placement_create parameter."); }
                break;
            case "Image":
                item["kind"] = "image";
                string file = Text(source["fileName"]) ?? throw new InvalidDataException("Image filename is unreadable.");
                if (!File.Exists(file))
                { throw new InvalidDataException("Image file is missing or unreadable: " + file); }
                string name = Guid.NewGuid().ToString("N") + Path.GetExtension(file);
                string folder = Path.Combine(importsRoot, "small-project-images");
                Directory.CreateDirectory(folder);
                File.Copy(file, Path.Combine(folder, name), overwrite: false);
                item["file"] = "small-project-images/" + name;
                item["corners"] = new JArray(new JArray(Point(source["lowerLeft"])), new JArray(Point(source["upperRight"])));
                NoteRotation(page, source);
                break;
            default:
                throw new InvalidDataException("Unsupported graphic mapping.");
        }
        if (source["layer"] is not null || source["pen"] is not null)
        { report.Note(page, Type(source), Id(source), "Exported layer/pen attributes have no placement_create parameters and remain comparison differences."); }
        return item;
    }

    private static bool IsEmptyText(JToken? content) => content is JValue { Type: JTokenType.String }
        ? string.IsNullOrWhiteSpace(Text(content))
        : content is JObject translations && translations.Properties().All(p => p.Value.Type == JTokenType.String && string.IsNullOrWhiteSpace(Text(p.Value)));

    private void NoteRotation(string page, JObject source)
    {
        if (Number(source["angle"]) is double angle && angle != 0)
        { report.Note(page, Type(source), Id(source), "Exported rotation has no placement_create parameter: " + angle.ToString(CultureInfo.InvariantCulture)); }
    }

    private async Task MovePropertiesAsync(string page, JObject[] objects)
    {
        JObject[] properties = objects.Where(o => Type(o) is "ContextPropertyPlacement" or "PropertyPlacement").ToArray();
        if (properties.Length == 0)
        { return; }
        foreach (IGrouping<string, JObject> group in properties.GroupBy(p => p["parentOid"]?.ToString() ?? "", StringComparer.Ordinal))
        {
            JObject? owner = objects.FirstOrDefault(o => Id(o) == group.Key && Type(o) is not ("PropertyPlacement" or "ContextPropertyPlacement"));
            if (owner is null)
            {
                foreach (JObject item in group)
                { report.Warning(page, Type(item) + " " + Id(item) + ": exported property parent is absent; no move can be identified."); }
                continue;
            }
            JObject[] slots = group.ToArray();
            // The exported propertyIndex is AnyPropertyId.Index, not the tool's collection slot. The default
            // target arrangement may differ, so use only an unambiguous displayed ID and the fresh target slot.
            foreach (int[] indexes in Enumerable.Range(0, slots.Length).Chunk(50))
            {
                JObject[] actual = await ReadDrawingsAsync(page, owner);
                JObject[] parents = actual.Where(a => a["parent"]?.Type is null or JTokenType.Null && DrawingMatches(owner, a)).ToArray();
                if (parents.Length != 1)
                {
                    foreach (int index in indexes)
                    { report.Warning(page, Type(slots[index]) + " " + Id(slots[index]) + ": target parent is absent or ambiguous; no property move was guessed."); }
                    continue;
                }
                JObject parent = parents[0];
                var moves = new List<(JObject Source, JObject Move)>();
                foreach (int index in indexes)
                {
                    JObject item = slots[index];
                    await ObserveAsync(page, item, () =>
                    {
                        string? propertyId = item["propertyId"]?.ToString();
                        JObject[] matches = actual.Where(a => JToken.DeepEquals(a["parent"], parent["locator"])
                            && Text(a["properties"]?["property_id"]) == propertyId).ToArray();
                        if (matches.Length != 1 || slots.Count(p => p["propertyId"]?.ToString() == propertyId) != 1
                            || !int.TryParse(Text(matches[0]["properties"]?["property_index"]), NumberStyles.None, CultureInfo.InvariantCulture, out int targetSlot))
                        { throw new InvalidDataException("Displayed property ID is absent or ambiguous, or the target collection slot is unreadable; no selector was guessed."); }
                        double[] offset = Point(item["location"]), origin = Point(owner["location"]);
                        double[] desired = [origin[0] + offset[0], origin[1] + offset[1]];
                        double angle = RequiredNumber(item["angle"]) * 180 / Math.PI;
                        int normalized = (int)Math.Round(angle) % 360;
                        if (normalized < 0)
                        { normalized += 360; }
                        if (Math.Abs(angle - Math.Round(angle)) > 0.000001 || normalized is not (0 or 90 or 180 or 270))
                        { throw new InvalidDataException("Property rotation cannot be represented by the tool's 0/90/180/270-degree contract."); }
                        double[] current = Point(matches[0]["location"]);
                        bool samePosition = Math.Abs(desired[0] - current[0]) <= 0.01 && Math.Abs(desired[1] - current[1]) <= 0.01;
                        bool sameAngle = double.TryParse(Text(matches[0]["properties"]?["angle_deg"]), NumberStyles.Float, CultureInfo.InvariantCulture, out double currentAngle)
                            && Math.Abs(normalized - currentAngle) <= 0.000001;
                        if (!samePosition || !sameAngle)
                        {
                            moves.Add((item, new JObject
                            {
                                ["parent"] = parent["locator"]!.DeepClone(),
                                ["property_id"] = item["propertyId"]!.DeepClone(),
                                ["property_index"] = targetSlot,
                                ["x_mm"] = desired[0],
                                ["y_mm"] = desired[1],
                                ["angle_deg"] = normalized,
                            }));
                        }
                        return Task.CompletedTask;
                    });
                }
                if (moves.Count > 0)
                {
                    await InvokeAsync(page, "eplan_placement_update", new JObject
                    {
                        ["page"] = page,
                        ["snap_to_grid"] = false,
                        ["property_moves"] = new JArray(moves.Select(m => m.Move)),
                    }, moves.Select(m => m.Source).ToArray());
                }
            }
        }
    }

    private async Task<JObject[]> ReadDrawingsAsync(string page, JObject source)
    {
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
        return result.ToArray();
    }

    private async Task ReviewAsync(string page, JObject source)
    {
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var args = new JObject { ["page"] = page, ["limit"] = 200 };
            if (cursor is not null)
            { args["cursor"] = cursor; }
            JObject? response = await InvokeAsync(page, "eplan_page_review", args, [source], write: false);
            if (response is null)
            { return; }
            cursor = Text(response["cursor"]);
            if (cursor is not null && !seen.Add(cursor))
            {
                report.Warning(page, "Page review cursor repeated; remaining issues are unreadable.");
                return;
            }
        } while (cursor is not null);
    }

    private bool DrawingMatches(JObject source, JObject actual)
    {
        if (Type(source) != Text(actual["kind"]))
        { return false; }
        if (!TryPoint(source["location"], out double[] sourcePoint) || !TryPoint(actual["location"], out double[] targetPoint)
            || Math.Abs(sourcePoint[0] - targetPoint[0]) > 0.01 || Math.Abs(sourcePoint[1] - targetPoint[1]) > 0.01)
        { return false; }
        string? sourceName = Text(source["name"]);
        if (sourceName is not null && _actualNames.GetValueOrDefault(sourceName, sourceName) != Text(actual["properties"]?["name"]))
        { return false; }
        if (source["symbol"] is JObject symbol && actual["symbol"] is JObject actualSymbol && !JToken.DeepEquals(symbol, actualSymbol))
        { return false; }
        return true;
    }

    private static HashSet<string> MacroMemberIds(JObject box, JObject[] objects)
    {
        var members = new HashSet<string>(StringComparer.Ordinal) { Id(box) };
        foreach (JObject child in Objects(box["functionsInside"]))
        { members.Add(Id(child)); }
        bool changed;
        do
        {
            int before = members.Count;
            foreach (JObject item in objects)
            {
                if (item["parentFunction"] is JObject parent && members.Contains(Id(parent)))
                { members.Add(Id(item)); }
                if (Type(item) == "Block" && item["memberOids"] is JArray blockMembers && blockMembers.Any(id => members.Contains(id.ToString())))
                {
                    members.Add(Id(item));
                    foreach (JToken id in blockMembers)
                    { members.Add(id.ToString()); }
                }
                if (item["blockOid"] is not null && members.Contains(item["blockOid"]!.ToString()))
                { members.Add(Id(item)); }
            }
            changed = members.Count > before;
        } while (changed);
        return members;
    }

    private static string DrawingKey(JObject drawing) => drawing["locator"]?["type"] + "/" + drawing["locator"]?["oid"];
    private static bool TryPoint(JToken? token, out double[] point)
    {
        try
        { point = Point(token); return true; }
        catch (InvalidDataException) { point = []; return false; }
    }
    private static double RequiredNumber(JToken? token) => Number(token) ?? throw new InvalidDataException("Required numeric field is absent or unreadable.");
}
