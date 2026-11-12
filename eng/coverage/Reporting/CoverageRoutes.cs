using System.Globalization;
using System.Text.Json.Serialization;

namespace EplanMcp.Coverage.Reporting;

internal sealed record RouteCount(int Count, [property: JsonPropertyName("live_verified")] int LiveVerified);

/// <summary>Disjoint call routes; existing evidence states and callable membership remain unchanged.</summary>
internal static class CoverageRoutes
{
    internal static readonly string[] Names = ["dedicated", "generic_read", "api_try_only", "state_only", "other", "declined", "none"];
    internal static readonly string[] Labels = ["专用工具", "通用读取", "仅通用入口", "仅状态覆盖", "其他可调用", "明确不做", "到不了"];

    /// <summary>
    /// Exclusion reasons that close a group on purpose (owner 2026-11-12): EPLAN marks the member internal, deprecated or not
    /// applicable, the project bans it, or policy forbids it (global settings writes, arbitrary paths, shared UI state).
    /// Technical obstacles (constructors, static members, master data copies) stay "none".
    /// </summary>
    internal static readonly string[] DeclinedReasons =
        ["catalog:internal", "catalog:deprecated", "catalog:banned_symbol", "catalog:not_applicable", "settings_write", "external_path_parameter", "shared_interactive_state"];

    /// <summary>
    /// Whole types closed on purpose (owner 2026-11-12), for groups no tool or api-try root reaches: property-ID tables and
    /// conversions (eplan_property_schema and eplan_property_get/set work by ID and name), progress windows (shared UI
    /// state), and the offline host application (Init, Exit, licence, dialogs) that an add-in inside EPLAN never drives.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> DeclinedTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["T:Eplan.EplApi.DataModel.Properties"] = "property_id_tables",
        ["T:Eplan.EplApi.DataModel.AnyPropertyId"] = "property_id_tables",
        ["T:Eplan.EplApi.Base.Progress"] = "shared_interactive_state",
        ["T:Eplan.EplApi.Base.IEplProgress"] = "shared_interactive_state",
        ["T:Eplan.EplApi.System.EplApplication"] = "offline_host_application",
        // What these types add over the ISettings read slice (ADR-0036) is writing and restructuring settings and schemes.
        ["T:Eplan.EplApi.Base.Settings"] = "settings_write",
        ["T:Eplan.EplApi.Base.SettingNode"] = "settings_write",
        ["T:Eplan.EplApi.Base.SchemeSetting"] = "settings_write",
    };

    /// <summary>Groups whose every api-try signature is excluded for a declined reason, with the most frequent reason.</summary>
    internal static Dictionary<string, string> Declined(string apiTryReport)
    {
        var reasons = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        foreach (string line in apiTryReport.Split('\n').Skip(1))
        {
            string[] cells = line.TrimEnd('\r').Split('\t');
            if (cells.Length < 3)
            {
                continue;
            }

            if (!reasons.TryGetValue(cells[0], out List<string?>? list))
            {
                reasons[cells[0]] = list = [];
            }

            list.Add(cells[2] == "excluded" && cells.Length > 3 && DeclinedReasons.Contains(cells[3], StringComparer.Ordinal) ? cells[3] : null);
        }

        return reasons.Where(r => r.Value.All(v => v is not null))
            .ToDictionary(r => r.Key, r => r.Value.GroupBy(v => v!, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key, StringComparer.Ordinal);
    }
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
