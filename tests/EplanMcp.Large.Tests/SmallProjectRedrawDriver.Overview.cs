using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

internal sealed partial class SmallProjectRedrawDriver
{
    private static readonly string[] _blockPointFields = ["location", "start", "end", "center", "lowerLeft", "upperRight"];
    private static readonly string[] _blockPointArrayFields = ["points", "boundingBox"];
    private readonly HashSet<string> _incompleteOverviewPages = new(StringComparer.Ordinal);

    private async Task InsertOverviewMacrosAsync(string page, JObject[] objects)
    {
        foreach (JObject box in objects.Where(o => Text(o["category"]) == "PLCBox" || Type(o) == "PLC"))
        {
            JObject? part = Objects(box["parts"]).FirstOrDefault();
            if (part is null)
            {
                report.Note(page, Type(box), Id(box), "PLC box has no exported part reference; using individual objects.");
                continue;
            }
            await ObserveAsync(page, box, async () =>
            {
                JObject? result = await InvokeAsync(page, "eplan_macro_insert", new JObject
                {
                    ["page"] = page,
                    ["part"] = part["part"]?.DeepClone(),
                    ["part_variant"] = part["variant"]?.DeepClone(),
                    ["representation"] = "Overview",
                    ["variant"] = (int?)box["symbol"]?["variant"] ?? 0,
                    ["x"] = OverviewMacroPosition.X,
                    ["y"] = OverviewMacroPosition.Y,
                    ["macro_box"] = false,
                }, [box]);
                if (result is null)
                {
                    report.Note(page, Type(box), Id(box), "Overview macro insertion failed; continuing with individual objects.");
                    return;
                }
                JObject[] cards = Objects(result["devices"]).Where(d => Text(d["category"]) == "PLCBox").ToArray();
                if (cards.Length != 1 || Text(cards[0]["tag"]) is not string inserted || Text(box["name"]) is not string wanted)
                {
                    report.Warning(page, "Inserted Overview PLC box has no unambiguous tag; rename and device coverage remain unverified.");
                    return;
                }
                if ((bool?)result["recovered"] != true && inserted != wanted)
                {
                    if (await InvokeAsync(page, "eplan_device_update", new JObject { ["tag"] = inserted, ["new_tag"] = wanted }, [box]) is null)
                    { return; }
                }
                // A85: commit changes suffix-bearing functions; only fresh post-commit names prove coverage.
                JObject[] actual = await ReadDrawingsAsync(page, box);
                if (!_lastDrawingReadComplete)
                {
                    report.Warning(page, "Overview macro device coverage is unverified because the post-rename drawing read was incomplete.");
                    return;
                }
                JObject[] boxes = actual.Where(a => Text(a["kind"]) == Type(box) && Text(a["properties"]?["name"]) == wanted).ToArray();
                if (boxes.Length == 1 && TryPoint(box["location"], out double[] from) && TryPoint(boxes[0]["location"], out double[] to))
                {
                    report.Note(page, Type(box), Id(box), FormattableString.Invariant(
                        $"Overview PLC box position: source=({from[0]},{from[1]}); target=({to[0]},{to[1]}); dx={to[0] - from[0]}; dy={to[1] - from[1]} (mm)."));
                }
                HashSet<string> members = MacroMemberIds(box, objects);
                foreach (JObject candidate in objects.Where(o => members.Contains(Id(o)) && IsDevice(o)))
                {
                    if (actual.Any(a => DrawingMatches(candidate, a)))
                    {
                        _covered.Add(Key(page, candidate));
                        report.Note(page, Type(candidate), Id(candidate), "Covered by the Overview macro after fresh name and geometry readback.");
                    }
                    else
                    { report.Warning(page, Type(candidate) + " " + Id(candidate) + ": Overview macro device readback did not match the source."); }
                }
                _placedTags.Add(wanted);
            });
        }
    }

    private async Task ReconcileOverviewGraphicsAsync(string page, JObject[] objects)
    {
        _incompleteOverviewPages.Add(page);
        JObject evidence = objects.FirstOrDefault() ?? new JObject { ["type"] = "Page" };
        JObject[] read = await ReadDrawingsAsync(page, evidence);
        if (!_lastDrawingReadComplete)
        {
            report.Warning(page, "Overview graphics reconciliation requires a complete drawing read; no deletion or duplicate creation was guessed.");
            return;
        }
        HashSet<string> blockMemberIds = objects.Where(o => Type(o) == "Block").SelectMany(BlockMemberIds).ToHashSet(StringComparer.Ordinal);
        var desired = objects.Where(o => IsGraphic(o) && o["blockOid"] is null or { Type: JTokenType.Null } && !blockMemberIds.Contains(Id(o))).ToList();
        bool sourceComplete = true;
        foreach (JObject block in objects.Where(o => Type(o) == "Block"))
        {
            if (read.Any(a => Text(a["kind"]) == "Block" && Text(a["properties"]?["name"]) == Text(block["name"])))
            { continue; }
            try
            {
                if (block["members"] is not JArray)
                { throw new InvalidDataException("Source block members are absent; export the source again before reconciling its graphics."); }
                desired.AddRange(AbsoluteBlockMembers(block).Where(IsGraphic));
            }
            catch (InvalidDataException error)
            {
                sourceComplete = false;
                report.Warning(page, "Block " + Id(block) + ": " + error.Message);
            }
        }
        var available = read.Where(IsUngroupedGraphic).ToList();
        var missing = new List<JObject>();
        foreach (JObject source in desired)
        {
            // Macro text lacks the complete source languages: always rebuild it, even at identical geometry.
            JObject? match = Type(source) is "Text" or "PathText" ? null : available.FirstOrDefault(a => GraphicMatches(source, a));
            if (match is null)
            {
                if (Id(source).Contains("/members/", StringComparison.Ordinal)
                    && read.Any(a => Text(a["properties"]?["grouped"]) == "true" && GraphicMatches(source, a)))
                { report.Warning(page, Type(source) + " " + Id(source) + ": corresponding target is already grouped, but its block name is unreadable; membership cannot be reconstructed without guessing."); }
                else
                { missing.Add(source); }
            }
            else
            {
                available.Remove(match);
                report.Note(page, Type(source), Id(source), "Overview graphic retained: type, geometry and exported style match within 0.01 mm.");
            }
        }
        foreach (JObject[] batch in (sourceComplete ? available : []).Chunk(100))
        {
            JObject? deleted = await InvokeAsync(page, "eplan_placement_delete", new JObject
            {
                ["page"] = page,
                ["items"] = new JArray(batch.Select(a => a["locator"]!.DeepClone())),
            }, batch.Select(a => new JObject { ["type"] = a["kind"]?.DeepClone(), ["oid"] = a["locator"]?["oid"]?.DeepClone() }).ToArray());
            if (deleted is null)
            {
                report.Warning(page, "Overview graphic deletion failed; replacement creation was not attempted because it could duplicate surviving text.");
                return;
            }
        }
        if (!sourceComplete)
        {
            foreach (JObject source in missing.Where(item => Type(item) is "Text" or "PathText").ToArray())
            {
                report.Warning(page, Type(source) + " " + Id(source)
                    + ": source block members are incomplete, so existing macro text could not be deleted; replacement was not created to avoid duplicate text.");
                missing.Remove(source);
            }
        }
        foreach (JObject source in missing)
        { report.Note(page, Type(source), Id(source), "Overview graphic missing or replaced; recreating from the complete source geometry, style and languages."); }
        await DrawGraphicsAsync(page, missing.ToArray());
        if (sourceComplete)
        { _incompleteOverviewPages.Remove(page); }
    }

    private async Task CreateOverviewBlocksAsync(string page, JObject[] objects)
    {
        foreach (JObject block in objects.Where(o => Type(o) == "Block"))
        {
            await ObserveAsync(page, block, async () =>
            {
                JObject[] actual = await ReadDrawingsAsync(page, block);
                if (!_lastDrawingReadComplete)
                { throw new InvalidDataException("Block creation requires complete target drawing evidence."); }
                string name = Text(block["name"]) ?? throw new InvalidDataException("Block name is unreadable.");
                if (actual.Any(a => Text(a["kind"]) == "Block" && Text(a["properties"]?["name"]) == name))
                { return; }
                if (_incompleteOverviewPages.Contains(page))
                { throw new InvalidDataException("Overview graphics reconciliation is incomplete; this missing block was not created because surviving macro text or source membership is unverified."); }
                JObject[] members = AbsoluteBlockMembers(block);
                if (members.Length is < 2 or > 1000)
                { throw new InvalidDataException("Block has no readable supported set of 2..1000 members; it was not created."); }
                var available = actual.Where(IsUngroupedDrawing).ToList();
                var locators = new JArray();
                foreach (JObject member in members)
                {
                    JObject? found = available.FirstOrDefault(a => IsGraphic(member) ? GraphicMatches(member, a) : DrawingMatches(member, a));
                    if (found is null)
                    { throw new InvalidDataException("Block member " + Id(member) + " (" + Type(member) + ") has no matching ungrouped target; the block was not created."); }
                    available.Remove(found);
                    locators.Add(found["locator"]!.DeepClone());
                }
                await InvokeAsync(page, "eplan_placement_create", new JObject
                {
                    ["page"] = page,
                    ["items"] = new JArray(new JObject { ["kind"] = "block", ["name"] = name, ["members"] = locators }),
                }, [block]);
            });
        }
    }

    private static JObject[] AbsoluteBlockMembers(JObject block)
    {
        double[] origin = Point(block["location"]);
        return Objects(block["members"]).Select((member, index) =>
        {
            var copy = (JObject)member.DeepClone();
            // Native cached wrappers may share an object ID; the detached member path remains unique.
            copy["oid"] = Id(block) + "/members/" + index.ToString(CultureInfo.InvariantCulture);
            foreach (string field in _blockPointFields)
            {
                if (copy[field] is not null)
                { copy[field] = OffsetPoint(copy[field], origin); }
            }
            foreach (string field in _blockPointArrayFields)
            {
                if (copy[field] is JArray points)
                { copy[field] = new JArray(points.Select(point => OffsetPoint(point, origin))); }
            }
            return copy;
        }).ToArray();
    }

    private static IEnumerable<string> BlockMemberIds(JObject block) => Objects(block["members"]).Select(Id)
        .Concat((block["memberOids"] as JArray ?? []).Select(id => id.ToString()));

    private static JObject OffsetPoint(JToken? point, double[] origin)
    {
        double[] value = Point(point);
        return new JObject { ["x"] = value[0] + origin[0], ["y"] = value[1] + origin[1] };
    }

    private static bool IsUngroupedGraphic(JObject actual) => Text(actual["kind"]) is "Line" or "Rectangle" or "PolyLine" or "Arc" or "Text" or "PathText" or "Image"
        && IsUngroupedDrawing(actual);

    private static bool IsUngroupedDrawing(JObject actual) => actual["parent"]?.Type is null or JTokenType.Null
        && Text(actual["properties"]?["grouped"]) != "true" && actual["locator"] is JObject;

    private static bool GraphicMatches(JObject source, JObject actual)
    {
        if (Type(source) != Text(actual["kind"]) || !ClosePoint(source["location"], actual["location"]))
        { return false; }
        JObject properties = actual["properties"] as JObject ?? new JObject();
        if (Type(source) is "Text" or "PathText")
        {
            // Geometry selects text for grouping only after source text was recreated by reconciliation.
            if (!CloseReadNumber(source["height"], properties["height_mm"]) || !SameAngle(source["angle"], properties["angle"]))
            { return false; }
        }
        else if (source["boundingBox"] is JArray bounds && actual["bounds"] is JArray actualBounds)
        {
            if (bounds.Count != 2 || !TryPoint(bounds[0], out double[] lower) || !TryPoint(bounds[1], out double[] upper))
            { return false; }
            double[] expected = [.. lower, .. upper];
            if (actualBounds.Count != expected.Length || expected.Where((number, index) => !Near(number, ReadNumber(actualBounds[index]))).Any())
            { return false; }
        }
        else
        { return false; }
        if (source["layer"] is not null && !CloseReadNumber(source["layer"], properties["layer"]))
        { return false; }
        if (source["alignment"] is not null && Text(source["alignment"]) != Text(properties["alignment"]))
        { return false; }
        if (source["angle"] is not null && !SameAngle(source["angle"], properties["angle"]))
        { return false; }
        if (source["pen"] is JObject pen)
        {
            foreach ((string from, string to) in new[] { ("color", "pen_color"), ("width", "pen_width_mm"), ("style", "pen_style"), ("patternLength", "pen_pattern_length"), ("lineEndType", "pen_line_end") })
            {
                if (pen[from] is not null && !CloseReadNumber(pen[from], properties[to]))
                { return false; }
            }
        }
        if (Type(source) == "PolyLine")
        {
            JArray? points = ParseArray(properties["points"]);
            JObject[] sourcePoints = Objects(source["points"]);
            if (points is null || points.Count != sourcePoints.Length || sourcePoints.Where((point, index) => !ClosePoint(point, points[index])).Any()
                || source["closed"]?.ToString().ToLowerInvariant() != Text(properties["closed"]))
            { return false; }
        }
        if (Type(source) == "Arc")
        {
            if (Text(source["kind"]) != Text(properties["arc_kind"]))
            { return false; }
            foreach ((string from, string to, bool angle) in Text(source["kind"]) == "Ellipse"
                ? new[] { ("firstRadius", "radius_x_mm", false), ("secondRadius", "radius_y_mm", false), ("ellipseAngle", "ellipse_angle", false) }
                : new[] { ("radius", "radius_mm", false), ("startAngle", "start_angle", true), ("endAngle", "end_angle", true) })
            {
                if (!(angle ? SameAngle(source[from], properties[to]) : from == "ellipseAngle"
                    ? CloseReadNumber(source[from], properties[to]) : CloseGeometryNumber(source[from], properties[to])))
                { return false; }
            }
        }
        return true;
    }

    private static JArray? ParseArray(JToken? value)
    {
        try
        { return value is JArray array ? array : Text(value) is string text ? JArray.Parse(text) : null; }
        catch (JsonReaderException) { return null; }
    }
    private static bool ClosePoint(JToken? left, JToken? right) => TryPoint(left, out double[] first) && TryPoint(right, out double[] second)
        && Near(first[0], second[0]) && Near(first[1], second[1]);
    private static double? ReadNumber(JToken? value) => Number(value) ?? (double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : null);
    private static bool Near(double value, double? other) => other is double number && Math.Abs(value - number) <= 0.0100000001;
    private static bool CloseGeometryNumber(JToken? left, JToken? right) => Number(left) is double value && Near(value, ReadNumber(right));
    private static bool CloseReadNumber(JToken? left, JToken? right) => Number(left) is double value && ReadNumber(right) is double other && Math.Abs(value - other) <= 0.000001;
    private static bool SameAngle(JToken? radians, JToken? degrees) => Number(radians) is double angle && ReadNumber(degrees) is double other
        && Math.Abs((angle == -16002 ? angle : angle * 180 / Math.PI) - other) <= 0.000001;
}
