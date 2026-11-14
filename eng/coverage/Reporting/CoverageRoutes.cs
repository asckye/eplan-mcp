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
        ["catalog:internal", "catalog:deprecated", "catalog:banned_symbol", "catalog:not_applicable", "settings_write", "external_path_parameter", "shared_interactive_state", "masterdata_management",
         // Owner 2026-11-14: a member that only returns an enumerator closes out with the enumerator types.
         "enumerator_type"];

    /// <summary>
    /// Whole types closed on purpose (owner 2026-11-12), for groups no tool or api-try root reaches: property-ID tables and
    /// conversions (eplan_property_schema and eplan_property_get/set work by ID and name), progress windows (shared UI
    /// state), and the offline host application (Init, Exit, licence, dialogs) that an add-in inside EPLAN never drives.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> DeclinedTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["T:Eplan.EplApi.DataModel.Properties"] = "property_id_tables",
        ["T:Eplan.EplApi.DataModel.AnyPropertyId"] = "property_id_tables",
        // The master-data side of the same tables: AllMD*PropIDs list property IDs, which eplan_property_schema serves.
        ["T:Eplan.EplApi.MasterData.Properties"] = "property_id_tables",
        ["T:Eplan.EplApi.Base.Progress"] = "shared_interactive_state",
        ["T:Eplan.EplApi.Base.IEplProgress"] = "shared_interactive_state",
        ["T:Eplan.EplApi.System.EplApplication"] = "offline_host_application",
        // What these types add over the ISettings read slice (ADR-0036) is writing and restructuring settings and schemes.
        ["T:Eplan.EplApi.Base.Settings"] = "settings_write",
        ["T:Eplan.EplApi.Base.SettingNode"] = "settings_write",
        ["T:Eplan.EplApi.Base.SchemeSetting"] = "settings_write",
        // Owner 2026-11-14, from the closeout list in docs/design/2026-11-14-api-closeout-list.md: these types are
        // not engineering capability, so each carries a reason instead of counting as unreached. Capability is
        // unchanged; the list also names the types that stay open because they are real capability.
        // Add-in infrastructure: loading, action registration, licence, user rights, project events. The add-in runs
        // inside EPLAN and never drives these.
        ["T:Eplan.EplApi.ApplicationFramework.UserRights"] = "addin_infrastructure",
        ["T:Eplan.EplApi.ApplicationFramework.IXMLProcessor"] = "addin_infrastructure",
        ["T:Eplan.EplApi.ApplicationFramework.LockingVectorProxy"] = "addin_infrastructure",
        ["T:Eplan.EplApi.ApplicationFramework.Action"] = "addin_infrastructure",
        ["T:Eplan.EplApi.ApplicationFramework.IExtendedProcessor"] = "addin_infrastructure",
        ["T:Eplan.EplApi.ApplicationFramework.License"] = "addin_infrastructure",
        ["T:Eplan.EplApi.Starter.AssemblyResolver"] = "addin_infrastructure",
        ["T:Eplan.EplApi.DataModel.ProjectEventsProvider"] = "addin_infrastructure",
        ["T:Eplan.EplApi.DataModel.StorableObjectContext"] = "addin_infrastructure",
        // Result messages of master-data operations and their enumerators: only reachable by calling the
        // master-data writes that are themselves excluded.
        ["T:Eplan.EplApi.MasterData.MDPartsDatabaseMessages"] = "masterdata_message_objects",
        ["T:Eplan.EplApi.MasterData.MDPartsMessage"] = "masterdata_message_objects",
        ["T:Eplan.EplApi.MasterData.TemplateMDMessage"] = "masterdata_message_objects",
        ["T:Eplan.EplApi.MasterData.MDPartsMessagesEnumerator"] = "masterdata_message_objects",
        ["T:Eplan.EplApi.MasterData.MDPartsMessagesRegisteredEnumerator"] = "masterdata_message_objects",
        // Decision contexts of modal dialogs, which ADR-0030 does not drive.
        ["T:Eplan.EplApi.Base.FileSelectDecisionContext"] = "dialog_decision_context",
        ["T:Eplan.EplApi.Base.ListSelectDecisionContext"] = "dialog_decision_context",
        ["T:Eplan.EplApi.Base.IContext"] = "dialog_decision_context",
        ["T:Eplan.EplApi.Base.Context"] = "dialog_decision_context",
        // The finder itself and the filter arguments it takes: eplan_object_find / eplan_object_get provide this.
        ["T:Eplan.EplApi.DataModel.DMObjectsFinder"] = "covered_by_object_find",
        ["T:Eplan.EplApi.DataModel.Placements3DFilter"] = "covered_by_object_find",
        ["T:Eplan.EplApi.DataModel.InterruptionPointsFilter"] = "covered_by_object_find",
        // Same as SettingNode above: beyond the read-only ISettings slice (ADR-0036) this is writing settings.
        ["T:Eplan.EplApi.DataModel.ProjectSettingNode"] = "settings_write",
        // Object locking vectors; the owner decided on 2026-10-27 against multi-user editing and writes lock all.
        ["T:Eplan.EplApi.DataModel.LockingVector"] = "locking_internals",
        // Creating, opening, closing and deleting projects, each already a dedicated tool with approval and backup.
        ["T:Eplan.EplApi.DataModel.ProjectManager"] = "lifecycle_or_creation",
        // System messages, which the system-message tool already reads.
        ["T:Eplan.EplApi.Base.SysMessagesCollection"] = "covered_by_tool",
        // Owner 2026-11-14: the verification and message services are abstract and the public catalogue holds no
        // concrete subclass at all (Verification's three children are abstract too), so no caller can construct one;
        // instances reach a caller only through EPLAN's own callbacks, which are not trial roots.
        ["T:Eplan.EplApi.EServices.Verification"] = "abstract_no_public_implementation",
        ["T:Eplan.EplApi.EServices.FunctionVerification"] = "abstract_no_public_implementation",
        ["T:Eplan.EplApi.EServices.InterruptionPointVerification"] = "abstract_no_public_implementation",
        ["T:Eplan.EplApi.EServices.PotentialVerification"] = "abstract_no_public_implementation",
        ["T:Eplan.EplApi.EServices.PartVerification"] = "abstract_no_public_implementation",
        ["T:Eplan.EplApi.EServices.Message"] = "abstract_no_public_implementation",
        // Owner 2026-11-14, second round. A collection's enumerator is a mechanical by-product: one trial call
        // cannot iterate, and the collection itself is what a caller reaches for.
        ["T:Eplan.EplApi.DataModel.MasterData.FunctionDefinitionEnumerator"] = "enumerator_type",
        ["T:Eplan.EplApi.DataModel.Graphics.PlaceHolder.VariablesClass.VariablesEnumerator"] = "enumerator_type",
        ["T:Eplan.EplApi.DataModel.Graphics.PlaceHolder.ValueSetsClass.ValueSetEnumerator"] = "enumerator_type",
        ["T:Eplan.EplApi.Base.SysMessagesEnumerator"] = "enumerator_type",
        // Owner 2026-11-14: macros are file-backed master data and eplan_macro_* covers them. ADR-0054 lists
        // WindowMacro among the handles it opens, but the only source is WindowMacro.Create, which writes a macro
        // file; opening a file-writing path through the generic entry buys nothing the macro tools do not provide.
        ["T:Eplan.EplApi.DataModel.MasterData.WindowMacro"] = "file_masterdata_macro_tools",
        ["T:Eplan.EplApi.DataModel.MasterData.Macro"] = "file_masterdata_macro_tools",
        ["T:Eplan.EplApi.DataModel.MasterData.PageMacro"] = "file_masterdata_macro_tools",
        ["T:Eplan.EplApi.DataModel.MasterData.SymbolMacro"] = "file_masterdata_macro_tools",
        ["T:Eplan.EplApi.DataModel.MasterData.PrePlanningMacro"] = "file_masterdata_macro_tools",
        // The layer table hangs off Macro.CustomLayerTable, so it closes out with the macros that own it.
        ["T:Eplan.EplApi.DataModel.MasterData.TransientLayer"] = "file_masterdata_macro_tools",
        ["T:Eplan.EplApi.DataModel.MasterData.TransientLayerTable"] = "file_masterdata_macro_tools",
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
