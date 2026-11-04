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
            if (IsPartMark(item))
            {
                report.RecordMacroVersionDifference(page, item, "source macro", "library macro", "A part macro's mark without an exact counterpart: it is never drawn by hand.");
                continue;
            }
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
        foreach ((JObject Source, JObject Item)[] batch in mapped.Chunk(20))
        {
            await InvokeItemsAsync(page, "eplan_placement_create", new JObject
            {
                ["page"] = page,
                ["snap_to_grid"] = false,
                ["items"] = new JArray(batch.Select(item => item.Item)),
            }, batch.Select(item => item.Source).ToArray());
        }
    }

    // Closed polylines set visible explicitly (19101 = 0) come from part macros, like the Phoenix mark.
    private static bool IsPartMark(JObject item) => Type(item) == "PolyLine" && (bool?)item["closed"] == true && Property(item, 19101) == "0";

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
                CopyAngle(source, item);
                break;
            case "PolyLine":
                item["kind"] = "polyline";
                item["points"] = new JArray((source["points"] as JArray ?? throw new InvalidDataException("Polyline vertices are unreadable."))
                    .Select(point => new JArray(Point(point))));
                if (source["closed"] is not null)
                { item["closed"] = source["closed"]!.DeepClone(); }
                if ((source["bulges"] as JArray)?.Any(value => Number(value) != 0) == true)
                { report.Warning(page, "PolyLine " + Id(source) + ": nonzero bulges have no mapping; straight segments retain a reported geometry difference."); }
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
                    // Equal native angles preserve a circle's orientation as well as its radius.
                    item["start_angle"] = RequiredNumber(source["startAngle"]) * 180 / Math.PI;
                    item["end_angle"] = RequiredNumber(source["endAngle"]) * 180 / Math.PI;
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
                CopyAngle(source, item);
                if (Text(source["alignment"]) is string alignment)
                { item["alignment"] = alignment; }
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
                if (Number(source["angle"]) is double imageAngle && imageAngle != 0)
                { report.Warning(page, "Image " + Id(source) + ": image rotation cannot be mapped by placement_create."); }
                break;
            default:
                throw new InvalidDataException("Unsupported graphic mapping.");
        }
        if (source["layer"] is { Type: not JTokenType.Null } layer)
        { item["layer"] = layer.DeepClone(); }
        else if (source.ContainsKey("layer"))
        { report.Warning(page, Type(source) + " " + Id(source) + ": exported layer is unreadable; the default layer remains unverified."); }
        if (source["pen"] is JObject sourcePen)
        {
            var pen = new JObject();
            foreach ((string from, string to, double defaultValue) in new[]
            {
                ("color", "color", -16002d), ("width", "width_mm", -16002d), ("style", "style", -16002d),
                ("patternLength", "pattern_length", 16002d), ("lineEndType", "line_end", 0d),
            })
            {
                if (sourcePen[from] is JToken value)
                {
                    if (Number(value) is not double number)
                    { report.Warning(page, Type(source) + " " + Id(source) + ": exported pen." + from + " is unreadable; its default remains unverified."); }
                    else if (number != defaultValue)
                    { pen[to] = value.DeepClone(); }
                }
            }
            if (pen.HasValues)
            { item["pen"] = pen; }
        }
        return item;
    }

    private static bool IsEmptyText(JToken? content) => content is JValue { Type: JTokenType.String }
        ? string.IsNullOrWhiteSpace(Text(content))
        : content is JObject translations && translations.Properties().All(p => p.Value.Type == JTokenType.String && string.IsNullOrWhiteSpace(Text(p.Value)));

    private static void CopyAngle(JObject source, JObject item)
    {
        if (Number(source["angle"]) is double angle)
        { item["angle"] = angle == -16002 ? angle : angle * 180 / Math.PI; }
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
