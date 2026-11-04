using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Tests;

/// <summary>Compares detached native page exports without loading EPLAN or using object IDs as identity.</summary>
internal static class PageExportComparer
{
    internal const double PositionTolerance = 0.01;

    // Property names and IDs are deliberately explicit: do not discard arbitrary dates, paths or user text.
    // Numeric IDs are documented by the installed DataModel XML (Page / Project property lists).
    internal static readonly IReadOnlyList<PageExportIgnoreRule> Ignored =
    [
        new("oid", "Native object IDs change when a page is redrawn; references are resolved through paired objects."),
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

    private static readonly HashSet<string> _ignoredNames = new(Ignored.Select(rule => rule.Member), StringComparer.Ordinal);
    private static readonly HashSet<string> _lengthFields = new(StringComparer.Ordinal)
    {
        "x", "y", "height", "width", "radius", "firstRadius", "secondRadius", "left", "right", "top", "bottom", "gridSize",
    };

    internal static PageExportComparison Compare(string sourceJson, string targetJson) =>
        Compare(JObject.Parse(sourceJson), JObject.Parse(targetJson));

    internal static PageExportComparison Compare(JObject source, JObject target)
    {
        var result = new PageExportComparison();
        CollectWarnings(source, "source", result.Warnings);
        CollectWarnings(target, "target", result.Warnings);
        bool reportPage = IsReportPage(source) || IsReportPage(target);
        string[] pageFields = reportPage ? ["report", "properties"] :
            ["name", "pageType", "description", "plotFrame", "size", "gridSize", "form", "report", "properties"];
        foreach (string field in pageFields)
        {
            Differences(Normalize(source[field], field, null), Normalize(target[field], field, null), field, result.PageDifferences);
        }
        if (reportPage)
        {
            return result;
        }

        List<JObject> sourcePlacements = Objects(source["placements"]);
        List<JObject> targetPlacements = Objects(target["placements"]);
        var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var targetIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var pairs = new List<ObjectPair>();
        PairGroups(sourcePlacements.Where(p => Type(p) != "PropertyPlacement"),
            targetPlacements.Where(p => Type(p) != "PropertyPlacement"), result, pairs, null);
        for (int index = 0; index < pairs.Count; index++)
        {
            ObjectPair pair = pairs[index];
            string identity = "paired:" + index.ToString(CultureInfo.InvariantCulture);
            AddIdentity(sourceIds, pair.Source["oid"], identity);
            AddIdentity(targetIds, pair.Target["oid"], identity);
        }
        PairGroups(sourcePlacements.Where(p => Type(p) == "PropertyPlacement"),
            targetPlacements.Where(p => Type(p) == "PropertyPlacement"), result, pairs,
            (left, right) => SameParent(left, right, sourceIds, targetIds));

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

    private static bool IsReportPage(JObject page) => (bool?)page["isReportPage"] == true || page["report"] is JContainer { Count: > 0 };
    private static List<JObject> Objects(JToken? value) => value is JArray array ? array.OfType<JObject>().ToList() : [];
    private static string Type(JObject value) => (string?)value["type"] ?? "Unknown";

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
            .Where(item => (type == "Connection" || SamePosition(left["location"], item.right["location"]))
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
                if (property.Name == "errors")
                {
                    continue; // Reported separately as warnings; an unreadable value is not proof of equivalence.
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
            if (member is "properties" or "pins" or "parts" or "functionsInside")
            {
                normalized = normalized.OrderBy(item => item.ToString(Formatting.None), StringComparer.Ordinal).ToList();
            }
            return new JArray(normalized);
        }
        return value?.DeepClone();
    }

    private static void Differences(JToken? source, JToken? target, string path, List<PageExportFieldDifference> differences)
    {
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
        string member = path[(path.LastIndexOf('.') + 1)..];
        if (JToken.DeepEquals(source, target) || (_lengthFields.Contains(member) && Close(source, target)))
        {
            return;
        }
        differences.Add(new(path, source?.DeepClone(), target?.DeepClone()));
    }

    private static bool Close(JToken? source, JToken? target) => IsNumber(source) && IsNumber(target)
        && Math.Abs((double)source! - (double)target!) <= PositionTolerance + 1e-10;

    private static bool IsNumber(JToken? value) => value?.Type is JTokenType.Integer or JTokenType.Float;

    private static bool IsReference(string member, string property) => property is "parentOid" or "sourceOid"
        || (property == "oid" && member is "start" or "end" or "parentFunction" or "functionsInside");

    private static void CollectReferenceWarnings(JToken? value, string path, string member, Dictionary<string, string> identities,
        List<PageExportReadWarning> warnings)
    {
        if (value is JObject obj)
        {
            foreach (JProperty property in obj.Properties().Where(p => p.Name != "errors"))
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

internal sealed class PageExportComparison
{
    public SortedDictionary<string, PageExportTypeComparison> Types { get; } = new(StringComparer.Ordinal);
    public List<PageExportFieldDifference> PageDifferences { get; } = [];
    public List<PageExportReadWarning> Warnings { get; } = [];
    public bool HasDifferences => PageDifferences.Count > 0 || Types.Values.Any(type => type.Missing.Count > 0 || type.Extra.Count > 0 || type.Different.Count > 0);
    public bool IsComplete => Warnings.Count == 0;
}
