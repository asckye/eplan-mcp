using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Reporting;

/// <summary>
/// One layer's units: registered, excluded (by reason; <c>overlay:</c> marks the overlay's), in the denominator, and of
/// those how many reach each status, each status counting the units at that level or above. Partial: implemented groups
/// of which only some counted overloads are used. Undocumented: units in the denominator without IntelliSense entry.
/// </summary>
internal sealed record LayerCount(
    int Registered, SortedDictionary<string, int> Excluded, int Denominator, int Implemented, int ByTools, int Partial, int AutomatedTest,
    int LiveVerified, int EnvBlocked, int Undocumented);

/// <summary>How many of the registered items the Bridge uses, where there is no percentage to give.</summary>
internal sealed record UsedCount(int Used, int Registered);

/// <summary>Denominator, implemented and live-verified units of one namespace or object type.</summary>
internal sealed record ScopeCount(string Scope, int Denominator, int Implemented, int LiveVerified);

/// <summary>A unit the Bridge implements, its status and the tools that reach it (none: infrastructure only).</summary>
internal sealed record UnitCoverage(string Unit, string Layer, string Status, bool Partial, IReadOnlyList<string> Tools);

/// <summary>What one tool covers and what backs it: the engineering-task view.</summary>
internal sealed record ToolCoverage(
    string Name, string Safety, IReadOnlyList<string> Operations, int ApiIds, int OperationGroups, int DataDictionary, int Dependencies, int Actions,
    string? Spec, int Small, int Medium, int Large, IReadOnlyList<string> ApiNotes, LiveRecord? LatestLive);

/// <summary>
/// catalog/coverage.json: the catalog, the scan, the tool mapping and the overlay combined, with the statuses of the
/// coverage design (section 4), which build on each other: implemented (a Bridge use reaches the unit), automated_test
/// (a tool that reaches it has Small or Medium tests, or its operations have Bridge.Core tests), live_verified (such a
/// tool also has a stated live record that passed: api-notes, a spec or the Large README, with date and version). A
/// tool-level fact stands for every unit the tool reaches, so the upper levels are upper bounds. A use only in generated
/// code that no operation reaches counts for nothing (<see cref="BridgeUsage.Counts"/>).
/// </summary>
internal sealed class CoverageReport
{
    public const string RelativePath = "catalog/coverage.json";

    private static readonly string[] _levels = ["implemented", "automated_test", "live_verified"];

    public required string EplanApi { get; init; }

    public required string Catalog { get; init; }

    public required bool SdkCatalog { get; init; }

    public required bool ActionCatalog { get; init; }

    /// <summary>operation (with operation.method and operation.property), data_dictionary, action (with action.type: rows with a TYPE).</summary>
    public required SortedDictionary<string, LayerCount> Layers { get; init; }

    public required UsedCount Dependencies { get; init; }

    public required UsedCount DictionaryProperties { get; init; }

    public required UsedCount ActionNames { get; init; }

    public required IReadOnlyList<ScopeCount> OperationsByNamespace { get; init; }

    public required IReadOnlyList<ScopeCount> UndocumentedByNamespace { get; init; }

    public required IReadOnlyList<ScopeCount> DataDictionaryByObject { get; init; }

    public required IReadOnlyList<ToolCoverage> Tools { get; init; }

    public required IReadOnlyList<UnitCoverage> Units { get; init; }

    /// <summary>IDs the Bridge uses that the catalog does not have (only for catalog parts that are present).</summary>
    public required IReadOnlyList<string> Unmatched { get; init; }

    public required IReadOnlyList<ApiRule> OverlayRules { get; init; }

    public required IReadOnlyList<Availability> Availability { get; init; }

    public static CoverageReport Build(string repositoryRoot)
    {
        BridgeUsage usage = CoverageJson.Read<BridgeUsage>(Path.Combine(repositoryRoot, BridgeUsage.RelativePath));
        ToolMapping mapping = ToolMapping.Build(repositoryRoot);
        Overlay overlay = Overlay.Load(repositoryRoot, [.. mapping.Tools.Select(t => t.Name)]);
        string directory = $"catalog/eplan-{string.Join('.', usage.EplanApi.Split('.').Take(3))}";
        ApiCatalog catalog = ApiCatalog.Load(Path.Combine(repositoryRoot, directory));

        ILookup<string, ToolEntry> toolsById = mapping.Tools.SelectMany(t => t.Api.Concat(t.Actions).Select(id => (id, t))).ToLookup(p => p.id, p => p.t);
        static bool Tested(ToolEntry t) => t.Evidence.Tests.Any(c => c.Size is "Small" or "Medium") || t.Evidence.OperationTests.Count > 0;
        int Level(string id) => toolsById[id].Any(t => Tested(t) && t.Live.Any(r => r.Verifies)) ? 2 : toolsById[id].Any(Tested) ? 1 : 0;
        var used = new Dictionary<string, (int Level, SortedSet<string> Tools, HashSet<string> Ids)>(StringComparer.Ordinal);
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var properties = new HashSet<string>(StringComparer.Ordinal);
        var actionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();
        foreach (string id in usage.Usages.Where(u => u.Value.Any(BridgeUsage.Counts)).Select(u => u.Key))
        {
            string? unit = catalog.Resolve(id);
            properties.UnionWith(catalog.PropertyOf(id) is { } property ? [property] : []);
            if (unit == "dependency")
            {
                dependencies.Add(id);
            }
            else if (unit is not null)
            {
                (int level, SortedSet<string> tools, HashSet<string> ids) = used.GetValueOrDefault(unit, (0, new(StringComparer.Ordinal), new(StringComparer.Ordinal)));
                tools.UnionWith(toolsById[id].Select(t => t.Name));
                ids.Add(id);
                used[unit] = (Math.Max(level, Level(id)), tools, ids);
                actionNames.UnionWith(catalog.Units[unit].Layer == ApiCatalog.Action ? [catalog.Units[unit].Scope] : []);
            }
            else if (id.StartsWith("A:", StringComparison.Ordinal) && catalog.HasAction(id))
            {
                actionNames.Add(id[2..]); // an action used without a constant TYPE: the action, but no row
            }
            else if (id.StartsWith("A:", StringComparison.Ordinal) ? catalog.HasActions : catalog.HasSdk)
            {
                unmatched.Add(id);
            }
        }

        Dictionary<string, string> status = catalog.Units.Values.ToDictionary(u => u.Key, u => Status(u, used, overlay), StringComparer.Ordinal);
        bool Partial(string key) => catalog.Groups.TryGetValue(key, out var members) && used.TryGetValue(key, out var use)
            && members.Count(m => !m.Excluded && use.Ids.Contains(m.Id)) < members.Count(m => !m.Excluded);
        bool Counted(Unit u) => !status[u.Key].StartsWith("excluded", StringComparison.Ordinal);
        Unit[] all = [.. catalog.Units.Values];
        LayerCount Count(Func<Unit, bool> filter) => CountLayer([.. all.Where(filter)], status, used, Partial);
        return new CoverageReport
        {
            EplanApi = usage.EplanApi,
            Catalog = directory,
            SdkCatalog = catalog.HasSdk,
            ActionCatalog = catalog.HasActions,
            Layers = new(StringComparer.Ordinal)
            {
                ["operation"] = Count(u => u.Layer == ApiCatalog.Operation),
                ["operation.method"] = Count(u => u.Layer == ApiCatalog.Operation && u.Kind == "method"),
                ["operation.property"] = Count(u => u.Layer == ApiCatalog.Operation && u.Kind == "property"),
                ["data_dictionary"] = Count(u => u.Layer == ApiCatalog.DataDictionary),
                ["action"] = Count(u => u.Layer == ApiCatalog.Action),
                ["action.type"] = Count(u => u.Layer == ApiCatalog.Action && u.Kind is not null),
            },
            Dependencies = new UsedCount(dependencies.Count, catalog.Dependencies),
            DictionaryProperties = new UsedCount(properties.Count(p => !catalog.Properties[p]), catalog.Properties.Count(p => !p.Value)),
            ActionNames = new UsedCount(actionNames.Count, all.Where(u => u.Layer == ApiCatalog.Action && Counted(u)).Select(u => u.Scope).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            OperationsByNamespace = Scopes(all.Where(u => u.Layer == ApiCatalog.Operation && Counted(u)), status),
            UndocumentedByNamespace = Scopes(all.Where(u => u.Layer == ApiCatalog.Operation && Counted(u) && !u.Documented), status),
            DataDictionaryByObject = [.. Scopes(all.Where(u => u.Layer == ApiCatalog.DataDictionary && Counted(u)), status).Where(s => s.Implemented > 0)],
            Tools = [.. mapping.Tools.Select(t => Tool(t, catalog))],
            Units = [.. used.Where(u => Counted(catalog.Units[u.Key]))
                .Select(u => new UnitCoverage(u.Key, catalog.Units[u.Key].Layer, status[u.Key], Partial(u.Key), [.. u.Value.Tools]))
                .OrderBy(u => u.Layer, StringComparer.Ordinal).ThenBy(u => u.Unit, StringComparer.Ordinal)],
            Unmatched = unmatched,
            OverlayRules = overlay.Api,
            Availability = overlay.Availability,
        };
    }

    /// <summary>The first overlay rule for a unit: N:&lt;namespace&gt; with its sub-namespaces, T:&lt;declaring type&gt;, or the unit's key.</summary>
    private static ApiRule? Rule(Unit unit, Overlay overlay) => overlay.Api.FirstOrDefault(r => r.Match.StartsWith("N:", StringComparison.Ordinal)
        ? unit.Layer == ApiCatalog.Operation && (unit.Scope == r.Match[2..] || unit.Scope.StartsWith(r.Match[2..] + ".", StringComparison.Ordinal))
        : string.Equals(r.Match, unit.Type, StringComparison.Ordinal) || string.Equals(r.Match, unit.Key, StringComparison.Ordinal));

    private static string Status(Unit unit, Dictionary<string, (int Level, SortedSet<string> Tools, HashSet<string> Ids)> used, Overlay overlay)
    {
        ApiRule? rule = Rule(unit, overlay);
        return unit.Exclusion is { } reason ? "excluded:" + reason
            : rule is { Status: "excluded" } ? "excluded:overlay:" + rule.Reason
            : used.TryGetValue(unit.Key, out var use) ? _levels[use.Level]
            : rule is { Status: "env_blocked" } ? "env_blocked"
            : "registered";
    }

    private static LayerCount CountLayer(
        Unit[] units, Dictionary<string, string> status, Dictionary<string, (int Level, SortedSet<string> Tools, HashSet<string> Ids)> used, Func<string, bool> partial)
    {
        Unit[] counted = [.. units.Where(u => !status[u.Key].StartsWith("excluded", StringComparison.Ordinal))];
        int AtLeast(int level) => counted.Count(u => Array.IndexOf(_levels, status[u.Key]) >= level);
        return new LayerCount(
            units.Length,
            new(units.Except(counted).GroupBy(u => status[u.Key][9..]).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal),
            counted.Length,
            AtLeast(0),
            counted.Count(u => used.TryGetValue(u.Key, out var use) && use.Tools.Count > 0),
            counted.Count(u => partial(u.Key)),
            AtLeast(1),
            AtLeast(2),
            counted.Count(u => status[u.Key] == "env_blocked"),
            counted.Count(u => !u.Documented));
    }

    private static List<ScopeCount> Scopes(IEnumerable<Unit> units, Dictionary<string, string> status) =>
    [
        .. units.GroupBy(u => u.Scope)
            .Select(g => new ScopeCount(g.Key, g.Count(), g.Count(u => Array.IndexOf(_levels, status[u.Key]) >= 0), g.Count(u => status[u.Key] == "live_verified")))
            .OrderBy(s => s.Scope, StringComparer.Ordinal),
    ];

    private static ToolCoverage Tool(ToolEntry tool, ApiCatalog catalog)
    {
        string?[] resolved = [.. tool.Api.Concat(tool.Actions).Select(catalog.Resolve)];
        Unit[] units = [.. resolved.Where(u => u is not null and not "dependency").Select(u => catalog.Units[u!]).Distinct()];
        int Of(string layer) => units.Count(u => u.Layer == layer);
        int Size(string size) => tool.Evidence.Tests.Count(c => c.Size == size);
        return new ToolCoverage(
            tool.Name, tool.Safety, tool.Operations, tool.Api.Count + tool.Actions.Count, Of(ApiCatalog.Operation), Of(ApiCatalog.DataDictionary),
            resolved.Count(u => u == "dependency"), Of(ApiCatalog.Action), tool.Evidence.Spec, Size("Small"), Size("Medium"), Size("Large"),
            tool.Evidence.ApiNotes, tool.Live.Where(r => r.Verifies).MaxBy(r => r.Date));
    }
}
