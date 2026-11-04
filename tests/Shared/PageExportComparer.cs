using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Tests;

/// <summary>Compares detached native page exports without loading EPLAN or using object IDs as identity.</summary>
internal static class PageExportComparer
{
    internal const double PositionTolerance = 0.01;
    internal const double AngleTolerance = 0.000001;

    // Property names and IDs are deliberately explicit: do not discard arbitrary dates, paths or user text.
    // Numeric IDs are documented by the installed DataModel XML (Page / Project property lists).
    internal static readonly IReadOnlyList<PageExportIgnoreRule> Ignored =
    [
        new("oid", "Native object IDs change when a page is redrawn; references are resolved through paired objects."),
        new("2000", "Object identification is the internal object ID encoded as a string; redrawing allocates a new ID."),
        new("10185", "Last editor's dongle license number identifies the editing session, not drawing content."),
        new("11001", "Automatic modification time belongs to the new page's edit history."),
        new("11002", "Creation time belongs to the new page's edit history."),
        new("11021", "Creation date belongs to the new page's edit history."),
        new("11024", "Automatic modification date belongs to the new page's edit history."),
        new("11020", "Page creator identifies the editing session, not drawing content."),
        new("11022", "Last editor sign-in identifies the editing session, not drawing content."),
        new("3011", "Last editor name identifies the editing session, not drawing content."),
        new("10045", "Full project directory changes between source and destination projects."),
        new("10009", "Full project filename includes the source or destination directory."),
        new("project", "Export request metadata identifies the source or destination project."),
        new("projectPath", "Export request metadata identifies the source or destination project."),
        new("projectFullName", "Export request metadata identifies the source or destination project."),
        new("editor", "Explicit export editor metadata is session-specific."),
        new("editorName", "Explicit export editor metadata is session-specific."),
        new("modified", "Explicit export modification metadata is session-specific."),
        new("Modified", "Explicit export modification metadata is session-specific."),
    ];

    // Names and scope verified against DataModel XML, PagePropertyList and ReportBlock.
    internal static readonly IReadOnlyList<PageExportSubsetRule> SubsetFields =
    [
        new("report[].deviceTag", "Report device tags", "The report's device set can include devices on pages outside the redrawn subset."),
        new("11004", "Number of pages per structure", "The structure can contain pages outside the redrawn subset."),
        new("11040", "Next page", "The next page can lie outside the redrawn subset."),
        new("11041", "Previous page", "The previous page can lie outside the redrawn subset."),
        new("11043", "Highest page number per structure", "The highest numbered page can lie outside the redrawn subset."),
        new("25020", "Counter page number", "The sequential page counter includes preceding pages outside the redrawn subset."),
        new("11067", "Report: Source project", "The report source identifies the original project rather than the subset project."),
        new("11011", "Page description (automatic report only)", "An automatic report description is generated from the report's device set, which can include omitted pages."),
    ];

    private static readonly HashSet<string> _ignoredNames = new(Ignored.Select(rule => rule.Member), StringComparer.Ordinal);
    private static readonly HashSet<string> _lengthFields = new(StringComparer.Ordinal)
    {
        "x", "y", "height", "width", "radius", "firstRadius", "secondRadius", "left", "right", "top", "bottom", "gridSize",
    };
    private static readonly string[] _memberGeometryFields =
        ["start", "end", "center", "points", "vertices", "radius", "firstRadius", "secondRadius", "width", "height", "angle", "ellipseAngle", "startAngle", "endAngle", "closed"];
    private static readonly HashSet<string> _angleFields = new(StringComparer.Ordinal)
        { "startAngle", "endAngle", "ellipseAngle", "angle", "rotation" };
    // Native placement angles are radians; these DataModel property values are degrees.
    private static readonly HashSet<string> _angleProperties = new(StringComparer.Ordinal)
        { "19323", "19324", "19342", "19343", "20451", "20869" };
    private static readonly Dictionary<string, string> _coordinateProperties = new(StringComparer.Ordinal)
    { ["19002"] = "x", ["19003"] = "y", ["19310"] = "x", ["19311"] = "y", ["19312"] = "x", ["19313"] = "y", ["19321"] = "x", ["19322"] = "y", ["19340"] = "x", ["19341"] = "y" };

    internal static PageExportComparison Compare(string sourceJson, string targetJson) =>
        Compare(JObject.Parse(sourceJson), JObject.Parse(targetJson));

    internal static PageExportComparison Compare(JObject source, JObject target)
    {
        var result = new PageExportComparison();
        CollectWarnings(source, "source", result.Warnings);
        CollectWarnings(target, "target", result.Warnings);
        bool reportPage = IsReportPage(source) || IsReportPage(target);
        string[] pageFields = reportPage ? ["report", "properties", "groups"] :
            ["name", "pageType", "description", "plotFrame", "size", "gridSize", "form", "report", "properties", "groups"];
        foreach (string field in pageFields)
        {
            JToken? left = Normalize(source[field], field, null);
            JToken? right = Normalize(target[field], field, null);
            Differences(field == "report" ? left ?? new JArray() : left, field == "report" ? right ?? new JArray() : right,
                field, result.PageDifferences);
        }
        SeparateSubsetDifferences(result, source, target, reportPage);
        if (reportPage)
        {
            result.ReportRows = CompareReportRows(source, target);
            return result;
        }

        List<JObject> sourcePlacements = Objects(source["placements"]);
        List<JObject> targetPlacements = Objects(target["placements"]);
        var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var targetIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var pairs = new List<ObjectPair>();
        PairGroups(sourcePlacements.Where(p => !IsPropertyPlacement(p)),
            targetPlacements.Where(p => !IsPropertyPlacement(p)), result, pairs, null);
        for (int index = 0; index < pairs.Count; index++)
        {
            ObjectPair pair = pairs[index];
            string identity = "paired:" + index.ToString(CultureInfo.InvariantCulture);
            AddIdentity(sourceIds, pair.Source["oid"], identity);
            AddIdentity(targetIds, pair.Target["oid"], identity);
        }
        int propertyPairStart = pairs.Count;
        PairGroups(sourcePlacements.Where(IsPropertyPlacement),
            targetPlacements.Where(IsPropertyPlacement), result, pairs,
            (left, right) => SameParent(left, right, sourceIds, targetIds));
        for (int index = propertyPairStart; index < pairs.Count; index++)
        {
            string identity = "paired:" + index.ToString(CultureInfo.InvariantCulture);
            AddIdentity(sourceIds, pairs[index].Source["oid"], identity);
            AddIdentity(targetIds, pairs[index].Target["oid"], identity);
        }

        PairGroup("Connection", Objects(source["connections"]), Objects(target["connections"]), result, pairs,
            (left, right) => SameEndpoints(left, right));
        CollectReferenceWarnings(source["placements"], "source.placements", "", sourceIds, result.Warnings);
        CollectReferenceWarnings(target["placements"], "target.placements", "", targetIds, result.Warnings);
        CollectReferenceWarnings(source["connections"], "source.connections", "", sourceIds, result.Warnings);
        CollectReferenceWarnings(target["connections"], "target.connections", "", targetIds, result.Warnings);
        foreach (ObjectPair pair in pairs)
        {
            var differences = new List<PageExportFieldDifference>();
            Differences(Normalize(pair.Source, "", sourceIds), Normalize(pair.Target, "", targetIds), "", differences);
            PageExportTypeComparison group = Group(result, pair.Type);
            if (differences.Count == 0)
            {
                group.Matched.Add(new(pair.Source, pair.Target));
            }
            else
            {
                group.Different.Add(new(pair.Source, pair.Target, differences));
            }
        }
        return result;
    }

    // Generated rows are computed from the project's data, partly from pages outside the redrawn subset:
    // they are checked per page and never counted as drawn objects.
    private static PageExportReportRows CompareReportRows(JObject source, JObject target)
    {
        static string Row(JObject row)
        {
            double x = (double?)row["location"]?["x"] ?? 0, y = (double?)row["location"]?["y"] ?? 0;
            string text = row["content"] is JObject content
                ? string.Join("|", content.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "@" + p.Value)) : "";
            return string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}) {2}", x, y, text);
        }
        static List<string> Rows(JObject page) => [.. Objects(page["placements"]).Where(p => (string?)p["type"] == "PlaceHolderText").Select(Row)];
        List<string> sourceOnly = Rows(source), targetOnly = [];
        foreach (string row in Rows(target))
        {
            if (!sourceOnly.Remove(row))
            { targetOnly.Add(row); }
        }
        int matched = Rows(target).Count - targetOnly.Count;
        return new(matched, sourceOnly, targetOnly);
    }

    private static bool IsReportPage(JObject page) => (bool?)page["isReportPage"] == true || page["report"] is JContainer { Count: > 0 };
    private static List<JObject> Objects(JToken? value) => value is JArray array ? array.OfType<JObject>().ToList() : [];
    private static string Type(JObject value) => (string?)value["type"] ?? "Unknown";
    private static bool IsPropertyPlacement(JObject value) => Type(value) is "PropertyPlacement" or "ContextPropertyPlacement";

    private static void SeparateSubsetDifferences(PageExportComparison result, JObject source, JObject target, bool reportPage)
    {
        bool automaticDescription = reportPage && Objects(source["report"]).Concat(Objects(target["report"]))
            .Any(report => (bool?)report["isAutomaticPageDescription"] == true);
        foreach (PageExportFieldDifference difference in result.PageDifferences.ToArray())
        {
            PageExportSubsetRule? rule = SubsetFields.FirstOrDefault(rule => rule.Member == "report[].deviceTag"
                ? difference.Field.StartsWith("report[", StringComparison.Ordinal) && difference.Field.EndsWith("].deviceTag", StringComparison.Ordinal)
                : (rule.Member != "11011" || automaticDescription) && IsPageProperty(difference.Field, rule.Member));
            if (rule is null)
            {
                continue;
            }
            result.PageDifferences.Remove(difference);
            result.SubsetDifferences.Add(new(difference.Field, rule.Name, rule.Reason, difference.Source, difference.Target));
        }
    }

    private static bool IsPageProperty(string path, string id) => path == "properties." + id
        || path.StartsWith("properties." + id + ".", StringComparison.Ordinal)
        || path.StartsWith("properties." + id + "[", StringComparison.Ordinal)
        || path.StartsWith("properties[id=" + id + ",index=", StringComparison.Ordinal);

    private static void PairGroups(IEnumerable<JObject> source, IEnumerable<JObject> target, PageExportComparison result,
        List<ObjectPair> pairs, Func<JObject, JObject, bool>? additionalKey)
    {
        Dictionary<string, List<JObject>> sourceGroups = source.GroupBy(Type).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        Dictionary<string, List<JObject>> targetGroups = target.GroupBy(Type).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (string type in sourceGroups.Keys.Union(targetGroups.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            PairGroup(type, sourceGroups.GetValueOrDefault(type) ?? [], targetGroups.GetValueOrDefault(type) ?? [], result, pairs, additionalKey);
        }
    }

    private static void PairGroup(string type, List<JObject> source, List<JObject> target, PageExportComparison result,
        List<ObjectPair> pairs, Func<JObject, JObject, bool>? additionalKey)
    {
        PageExportTypeComparison group = Group(result, type);
        var candidates = source.Select(left => target.Select((right, index) => (right, index))
            .Where(item => (type == "Connection" || type == "Block" || SamePosition(left["location"], item.right["location"]))
                && JToken.DeepEquals(left["symbol"], item.right["symbol"])
                && (additionalKey?.Invoke(left, item.right) ?? true))
            .OrderBy(item => DifferenceCount(left, item.right)).ThenBy(item => item.index)
            .Select(item => item.index).ToArray()).ToArray();
        int[] targetMatches = Enumerable.Repeat(-1, target.Count).ToArray();
        // Augmenting paths prevent a near-position candidate from consuming another object's only possible match.
        foreach (int index in Enumerable.Range(0, source.Count).OrderBy(i => candidates[i].Length))
        {
            Match(index, candidates, targetMatches, new bool[target.Count]);
        }
        var matchedSources = new HashSet<int>();
        for (int index = 0; index < target.Count; index++)
        {
            if (targetMatches[index] is int matched && matched >= 0)
            {
                matchedSources.Add(matched);
                pairs.Add(new(type, source[matched], target[index]));
            }
            else
            {
                group.Extra.Add((JObject)target[index].DeepClone());
            }
        }
        for (int index = 0; index < source.Count; index++)
        {
            if (!matchedSources.Contains(index))
            {
                group.Missing.Add((JObject)source[index].DeepClone());
            }
        }
    }

    private static bool Match(int source, int[][] candidates, int[] targetMatches, bool[] visited)
    {
        // Prefer a free exact candidate before displacing an equally good existing match.
        foreach (int target in candidates[source])
        {
            if (!visited[target] && targetMatches[target] < 0)
            {
                visited[target] = true;
                targetMatches[target] = source;
                return true;
            }
        }
        foreach (int target in candidates[source])
        {
            if (visited[target])
            {
                continue;
            }
            visited[target] = true;
            if (Match(targetMatches[target], candidates, targetMatches, visited))
            {
                targetMatches[target] = source;
                return true;
            }
        }
        return false;
    }

    private static int DifferenceCount(JObject left, JObject right)
    {
        var differences = new List<PageExportFieldDifference>();
        Differences(Normalize(left, "", null), Normalize(right, "", null), "", differences);
        return differences.Count;
    }

    private static bool SamePosition(JToken? left, JToken? right)
    {
        if (left is not JObject || right is not JObject)
        {
            return JToken.DeepEquals(left, right);
        }
        return Close(left["x"], right["x"]) && Close(left["y"], right["y"]);
    }

    private static bool SameParent(JObject left, JObject right, Dictionary<string, string> sourceIds, Dictionary<string, string> targetIds) =>
        Identity(sourceIds, left["parentOid"]) is string source && Identity(targetIds, right["parentOid"]) is string target && source == target;

    private static bool SameEndpoints(JObject left, JObject right) =>
        JToken.DeepEquals(Endpoint(left["start"]), Endpoint(right["start"]))
        && JToken.DeepEquals(Endpoint(left["end"]), Endpoint(right["end"]));

    private static JToken? Endpoint(JToken? value) => value is JObject endpoint
        ? new JObject
        {
            ["name"] = endpoint["name"]?.DeepClone(),
            ["pinName"] = endpoint["pinName"]?.DeepClone(),
            ["pinIndex"] = (endpoint["pinIndex"] ?? endpoint["index"])?.DeepClone(),
        }
        : value;

    private static string? Identity(Dictionary<string, string> identities, JToken? oid) =>
        oid is not null && identities.TryGetValue(oid.ToString(Formatting.None), out string? identity) ? identity : null;

    private static void AddIdentity(Dictionary<string, string> identities, JToken? oid, string identity)
    {
        if (oid is not null && oid.Type != JTokenType.Null)
        {
            identities[oid.ToString(Formatting.None)] = identity;
        }
    }

    private static JToken? Normalize(JToken? value, string member, Dictionary<string, string>? identities)
    {
        if (value is JObject obj)
        {
            var normalized = new JObject();
            foreach (JProperty property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (Type(obj) == "Block" && property.Name == "location")
                {
                    // A block's reference point can move without changing any of its absolute geometry.
                    continue;
                }
                if (Type(obj) == "Block" && property.Name == "members" && property.Value is JArray members)
                {
                    normalized[property.Name] = Normalize(new JArray(members.Select(item => AbsoluteMember(item, obj["location"]))), property.Name, identities);
                    continue;
                }
                if (Type(obj) == "Block" && property.Name == "properties" && property.Value is JArray blockProperties)
                {
                    normalized[property.Name] = Normalize(BlockReferenceProperties(blockProperties, obj["location"]), property.Name, identities);
                    continue;
                }
                if (property.Name is "errors" or "memberOids")
                {
                    // Read errors are warnings; block membership is compared using detached member content, not unstable IDs.
                    continue;
                }
                if (property.Name == "blockOid")
                {
                    normalized[property.Name] = NormalizeBlockReference(property.Value, identities);
                    continue;
                }
                if (property.Name is "oid" or "parentOid" or "sourceOid")
                {
                    bool reference = IsReference(member, property.Name);
                    if (reference && identities is not null && Identity(identities, property.Value) is string identity)
                    {
                        normalized[property.Name] = identity;
                    }
                    continue;
                }
                if (_ignoredNames.Contains(property.Name))
                {
                    continue;
                }
                normalized[property.Name] = Normalize(property.Value, property.Name, identities);
            }
            return normalized;
        }
        if (value is JArray array)
        {
            IEnumerable<JToken> items = array;
            if (member == "properties")
            {
                items = items.Where(item => !_ignoredNames.Contains((string?)item["id"] ?? ""));
            }
            var normalized = items.Select(item => Normalize(item, member, identities)!).ToList();
            if (member is "pins" or "parts" or "functionsInside")
            {
                normalized = normalized.OrderBy(item => item.ToString(Formatting.None), StringComparer.Ordinal).ToList();
            }
            if (member == "members")
            {
                normalized = normalized.OrderBy(item => (string?)item["type"], StringComparer.Ordinal)
                    .ThenBy(item => (double?)item["location"]?["x"])
                    .ThenBy(item => (double?)item["location"]?["y"])
                    .ThenBy(MemberGeometry, StringComparer.Ordinal)
                    .ThenBy(item => item.ToString(Formatting.None), StringComparer.Ordinal).ToList();
            }
            if (member == "groups")
            {
                normalized = normalized.OrderBy(item => (double?)item["location"]?["x"])
                    .ThenBy(item => (double?)item["location"]?["y"])
                    .ThenBy(item => item.ToString(Formatting.None), StringComparer.Ordinal).ToList();
            }
            return new JArray(normalized);
        }
        return value?.DeepClone();
    }

    private static JToken NormalizeBlockReference(JToken oid, Dictionary<string, string>? identities) =>
        identities is not null && Identity(identities, oid) is string identity ? new JValue(identity) : oid.DeepClone();

    private static JArray BlockReferenceProperties(JArray properties, JToken? origin)
    {
        var normalized = (JArray)properties.DeepClone();
        foreach (JObject property in normalized.OfType<JObject>())
        {
            // Block.INSTANCE_XCOORD/YCOORD repeat the reference point. Compare their offset from
            // Location so an inconsistent exported value remains visible rather than discarding it.
            string? axis = (int?)property["id"] switch { 19002 => "x", 19003 => "y", _ => null };
            if (axis is not null && IsNumber(origin?[axis])
                && double.TryParse(property["value"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double coordinate))
            { property["value"] = coordinate - (double)origin![axis]!; }
        }
        return normalized;
    }

    private static JToken AbsoluteMember(JToken value, JToken? origin)
    {
        JToken copy = value.DeepClone();
        if (copy is not JObject member || origin is not JObject || !IsNumber(origin["x"]) || !IsNumber(origin["y"]))
        { return copy; }
        foreach (string field in new[] { "location", "start", "end", "center", "lowerLeft", "upperRight", "boundingBox", "points", "vertices", "logicalArea" })
        { TranslateCoordinates(member[field], (double)origin["x"]!, (double)origin["y"]!); }
        foreach (JObject property in Objects(member["properties"]))
        {
            string id = property["id"]?.ToString() ?? "";
            if (_coordinateProperties.TryGetValue(id, out string? axis)
                && double.TryParse(property["value"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double coordinate))
            { property["value"] = coordinate + (double)origin[axis]!; }
            else if (id == "19103")
            {
                // The native display property contains the same relative location, rounded to two decimals.
                Match match = Regex.Match(property["value"]?.ToString() ?? "", @"^X:\s*([-+\d.eE]+)\s+mm\s+Y:\s*([-+\d.eE]+)\s+mm$");
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    && double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                { property["value"] = new JObject { ["x"] = x + (double)origin["x"]!, ["y"] = y + (double)origin["y"]! }; }
            }
        }
        // Nested blocks retain their translated origin so their own members can be expanded recursively.
        return member;
    }

    private static void TranslateCoordinates(JToken? value, double x, double y)
    {
        if (value is JObject point)
        {
            if (IsNumber(point["x"]) && IsNumber(point["y"]))
            {
                point["x"] = (double)point["x"]! + x;
                point["y"] = (double)point["y"]! + y;
            }
            else
            {
                foreach (JProperty property in point.Properties())
                { TranslateCoordinates(property.Value, x, y); }
            }
        }
        else if (value is JArray points)
        {
            foreach (JToken item in points)
            { TranslateCoordinates(item, x, y); }
        }
    }

    private static string MemberGeometry(JToken member) => new JObject(
        _memberGeometryFields
            .Where(name => member[name] is not null).Select(name => new JProperty(name,
                _angleFields.Contains(name) ? AngleOrderValue(member[name]!) : member[name]!.DeepClone())))
        .ToString(Formatting.None);

    private static JToken AngleOrderValue(JToken value)
    {
        if (!double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double angle)
            || !double.IsFinite(angle) || angle == -16002)
        { return value.DeepClone(); }
        double normalized = (angle % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        // Canonicalize the sorting key only; Differences still checks the unrounded exported angle.
        return new JValue(Math.Min(normalized, 2 * Math.PI - normalized) <= AngleTolerance ? 0 : Math.Round(normalized, 6));
    }

    private static void Differences(JToken? source, JToken? target, string path, List<PageExportFieldDifference> differences)
    {
        string member = path[(path.LastIndexOf('.') + 1)..];
        if (member == "properties" && source is null or JArray && target is null or JArray)
        {
            PropertyDifferences(source, target, path, differences);
            return;
        }
        if (member == "properties" && source is null && target is JObject targetProperties)
        {
            Differences(new JObject(), targetProperties, path, differences);
            return;
        }
        if (member == "properties" && source is JObject sourceProperties && target is null)
        {
            Differences(sourceProperties, new JObject(), path, differences);
            return;
        }
        if (path.StartsWith("report[", StringComparison.Ordinal) && path.IndexOf('.', StringComparison.Ordinal) < 0
            && (source is JObject && target is null || source is null && target is JObject))
        {
            Differences(source ?? new JObject(), target ?? new JObject(), path, differences);
            return;
        }
        if (source is JObject sourceObject && target is JObject targetObject)
        {
            foreach (string name in sourceObject.Properties().Select(p => p.Name).Union(targetObject.Properties().Select(p => p.Name), StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                Differences(sourceObject[name], targetObject[name], path.Length == 0 ? name : path + "." + name, differences);
            }
            return;
        }
        if (source is JArray sourceArray && target is JArray targetArray)
        {
            for (int index = 0; index < Math.Max(sourceArray.Count, targetArray.Count); index++)
            {
                Differences(index < sourceArray.Count ? sourceArray[index] : null, index < targetArray.Count ? targetArray[index] : null,
                    path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", differences);
            }
            return;
        }
        if (JToken.DeepEquals(source, target) || (_lengthFields.Contains(member) && Close(source, target))
            || (_angleFields.Contains(member) && SameAngle(source, target, 2 * Math.PI))
            || (_angleProperties.Contains(member) && SameAngle(source, target, 360)))
        {
            return;
        }
        differences.Add(new(path, source?.DeepClone(), target?.DeepClone()));
    }

    private static void PropertyDifferences(JToken? source, JToken? target, string path, List<PageExportFieldDifference> differences)
    {
        ILookup<(string Id, string Index), JObject> targets = Objects(target).ToLookup(PropertyKey);
        foreach (JObject property in Objects(source).OrderBy(p => (int?)p["id"]).ThenBy(p => (int?)p["index"]))
        {
            (string id, string index) = PropertyKey(property);
            JObject? counterpart = targets[(id, index)].FirstOrDefault();
            string valuePath = path + "[id=" + id + ",index=" + index + "].value";
            if (counterpart is null)
            {
                differences.Add(new(valuePath, property["value"]?.DeepClone(), null));
            }
            else
            {
                if (!(_angleProperties.Contains(id) && SameAngle(property["value"], counterpart["value"], 360))
                    && !(_coordinateProperties.ContainsKey(id) && Close(property["value"], counterpart["value"])))
                { Differences(property["value"], counterpart["value"], valuePath, differences); }
            }
        }
        // The owner's acceptance is a complete match: a property only the redraw has is a difference too.
        var sources = Objects(source).Select(PropertyKey).ToHashSet();
        foreach (JObject property in Objects(target).Where(p => !sources.Contains(PropertyKey(p))).OrderBy(p => (int?)p["id"]).ThenBy(p => (int?)p["index"]))
        {
            (string id, string index) = PropertyKey(property);
            differences.Add(new(path + "[id=" + id + ",index=" + index + "].value", null, property["value"]?.DeepClone()));
        }
    }

    private static (string Id, string Index) PropertyKey(JObject property) =>
        (property["id"]?.ToString(Formatting.None) ?? "null", property["index"]?.ToString(Formatting.None) ?? "null");

    private static bool Close(JToken? source, JToken? target) => IsNumber(source) && IsNumber(target)
        && Math.Abs((double)source! - (double)target!) <= PositionTolerance + 1e-10;

    private static bool IsNumber(JToken? value) => value?.Type is JTokenType.Integer or JTokenType.Float;

    private static bool SameAngle(JToken? source, JToken? target, double fullTurn)
    {
        if (!double.TryParse(source?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double left)
            || !double.TryParse(target?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double right)
            || !double.IsFinite(left) || !double.IsFinite(right))
        { return false; }
        if (left == -16002 || right == -16002)
        { return left == right; }
        double difference = Math.Abs((left - right) % fullTurn);
        return Math.Min(difference, fullTurn - difference) <= AngleTolerance;
    }

    private static bool IsReference(string member, string property) => property is "parentOid" or "sourceOid" or "blockOid"
        || (property == "oid" && member is "start" or "end" or "parentFunction" or "functionsInside");

    private static void CollectReferenceWarnings(JToken? value, string path, string member, Dictionary<string, string> identities,
        List<PageExportReadWarning> warnings)
    {
        if (value is JObject obj)
        {
            foreach (JProperty property in obj.Properties().Where(p => p.Name is not "errors" and not "memberOids"))
            {
                string propertyPath = path + "." + property.Name;
                if (IsReference(member, property.Name) && property.Value.Type != JTokenType.Null && Identity(identities, property.Value) is null)
                {
                    warnings.Add(new(propertyPath, new JObject
                    {
                        ["member"] = property.Name,
                        ["error"] = "Referenced owner is absent or could not be paired; identity equivalence is unverified.",
                        ["oid"] = property.Value.DeepClone(),
                    }));
                }
                CollectReferenceWarnings(property.Value, propertyPath, property.Name, identities, warnings);
            }
        }
        else if (value is JArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                CollectReferenceWarnings(array[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", member, identities, warnings);
            }
        }
    }

    private static PageExportTypeComparison Group(PageExportComparison result, string type)
    {
        if (!result.Types.TryGetValue(type, out PageExportTypeComparison? group))
        {
            group = new();
            result.Types.Add(type, group);
        }
        return group;
    }

    private static void CollectWarnings(JToken value, string path, List<PageExportReadWarning> warnings)
    {
        if (value is JObject obj)
        {
            if (Type(obj) == "Block" && obj["members"] is not JArray)
            {
                warnings.Add(new(path + ".members", new JObject
                {
                    ["member"] = "members",
                    ["error"] = "Detached block members are missing or unreadable; membership equivalence is unverified.",
                }));
            }
            if (obj["errors"] is JArray errors)
            {
                warnings.AddRange(errors.Select(error => new PageExportReadWarning(path, error.DeepClone())));
            }
            if (obj["unhandledTypes"] is JObject unhandled)
            {
                warnings.AddRange(unhandled.Properties().Where(p => (int?)p.Value > 0)
                    .Select(p => new PageExportReadWarning(path + ".unhandledTypes", new JObject { [p.Name] = p.Value.DeepClone() })));
            }
            foreach (JProperty property in obj.Properties().Where(p => p.Name != "errors"))
            {
                CollectWarnings(property.Value, path + "." + property.Name, warnings);
            }
        }
        else if (value is JArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                CollectWarnings(array[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", warnings);
            }
        }
    }

    private sealed record ObjectPair(string Type, JObject Source, JObject Target);
}

internal sealed record PageExportIgnoreRule(string Member, string Reason);
internal sealed record PageExportSubsetRule(string Member, string Name, string Reason);
internal sealed record PageExportSubsetDifference(string Field, string Name, string Reason, JToken? Source, JToken? Target);
internal sealed record PageExportFieldDifference(string Field, JToken? Source, JToken? Target);
internal sealed record PageExportReadWarning(string Path, JToken Error);
internal sealed record PageExportObjectMatch(JObject Source, JObject Target);
internal sealed record PageExportObjectDifference(JObject Source, JObject Target, IReadOnlyList<PageExportFieldDifference> Fields);

internal sealed class PageExportTypeComparison
{
    public List<PageExportObjectMatch> Matched { get; } = [];
    public List<JObject> Missing { get; } = [];
    public List<JObject> Extra { get; } = [];
    public List<PageExportObjectDifference> Different { get; } = [];
}

internal sealed record PageExportReportRows(int Matched, IReadOnlyList<string> SourceOnly, IReadOnlyList<string> TargetOnly);

internal sealed class PageExportComparison
{
    public SortedDictionary<string, PageExportTypeComparison> Types { get; } = new(StringComparer.Ordinal);
    public List<PageExportFieldDifference> PageDifferences { get; } = [];
    public List<PageExportSubsetDifference> SubsetDifferences { get; } = [];
    public int SubsetDifferenceCount => SubsetDifferences.Count;
    public PageExportReportRows? ReportRows { get; set; }
    public List<PageExportReadWarning> Warnings { get; } = [];
    public bool HasDifferences => PageDifferences.Count > 0 || Types.Values.Any(type => type.Missing.Count > 0 || type.Extra.Count > 0 || type.Different.Count > 0);
    public bool IsComplete => Warnings.Count == 0;
}
