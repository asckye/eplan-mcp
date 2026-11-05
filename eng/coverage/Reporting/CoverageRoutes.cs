using System.Globalization;
using System.Text.Json.Serialization;

namespace EplanMcp.Coverage.Reporting;

internal sealed record RouteCount(int Count, [property: JsonPropertyName("live_verified")] int LiveVerified);

/// <summary>Disjoint call routes; existing evidence states and callable membership remain unchanged.</summary>
internal static class CoverageRoutes
{
    internal static readonly string[] Names = ["dedicated", "generic_read", "api_try_only", "state_only", "other", "none"];
    internal static readonly string[] Labels = ["专用工具", "通用读取", "仅通用入口", "仅状态覆盖", "其他可调用", "到不了"];
    private static readonly HashSet<string> _generic = new(StringComparer.Ordinal)
        { "eplan_api_try", "eplan_api_get", "eplan_api_search", "eplan_action_try", "eplan_action_run" };
    private static readonly HashSet<string> _readGeneric = new(StringComparer.Ordinal)
        { "eplan_object_get", "eplan_object_find", "eplan_property_get", "eplan_property_schema", "eplan_property_set" };

    internal static string Classify(IReadOnlyList<string> tools, string status, bool callable) =>
        tools.Any(t => !_generic.Contains(t) && !_readGeneric.Contains(t)) ? "dedicated"
        : tools.Any(_readGeneric.Contains) ? "generic_read"
        : tools.Any(_generic.Contains) ? "api_try_only"
        : status is "implemented" or "automated_test" or "live_verified" ? "state_only"
        : callable ? "other" : "none";

    internal static SortedDictionary<string, RouteCount> Count(IEnumerable<(string Route, bool LiveVerified)> units)
    {
        var counts = new SortedDictionary<string, RouteCount>(Names.ToDictionary(n => n, _ => new RouteCount(0, 0)), StringComparer.Ordinal);
        foreach ((string route, bool live) in units)
        {
            RouteCount count = counts[route];
            counts[route] = new(count.Count + 1, count.LiveVerified + (live ? 1 : 0));
        }
        return counts;
    }

    internal static string Headline(int denominator, IReadOnlyDictionary<string, RouteCount> routes) => string.Create(CultureInfo.InvariantCulture,
        $"操作层 {denominator} 组：{string.Join("，", Names.Select((name, i) => Labels[i] + " " + routes[name].Count.ToString(CultureInfo.InvariantCulture)))}；实机验证 {routes.Values.Sum(c => c.LiveVerified)}");
}
