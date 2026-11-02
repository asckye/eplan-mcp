using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EplanMcp.Contracts;
using EplanMcp.Coverage.Mapping;

namespace EplanMcp.Coverage.Reporting;

/// <summary>Reachability uses the existing denominator without promoting any unit's evidence status.</summary>
internal sealed record OperationReachability(int Denominator, int Tools, int ApiTry, int Callable, SortedDictionary<string, int> OutsideDenominator,
    [property: JsonPropertyName("refused_unsafe")] int RefusedUnsafe);

internal sealed record ActionReachability(int Denominator, int Tools, int ActionTry);

internal sealed record NamespaceReachability(string Scope, int Callable, [property: JsonPropertyName("refused_unsafe")] int RefusedUnsafe);

internal sealed record CallableCoverage(OperationReachability Operation, ActionReachability Action, IReadOnlyList<NamespaceReachability> OperationsByNamespace)
{
    internal static CallableCoverage Build(IEnumerable<Unit> units, IReadOnlyDictionary<string, string> status,
        string trials, IEnumerable<string> propertyGroups, IEnumerable<string> actionWhitelist, IReadOnlyList<ApiRule>? rules = null, IEnumerable<string>? writablePropertyGroups = null)
    {
        Unit[] all = [.. units];
        bool Counted(Unit u) => !status[u.Key].StartsWith("excluded", StringComparison.Ordinal);
        bool Implemented(Unit u) => status[u.Key] is "implemented" or "automated_test" or "live_verified";
        Unit[] operations = [.. all.Where(u => u.Layer == ApiCatalog.Operation && Counted(u))];
        HashSet<string> denominator = operations.Select(u => u.Key).ToHashSet(StringComparer.Ordinal);
        ApiRule[] refused = [.. (rules ?? []).Where(r => r.Status == "refused" && r.Reason == "unsafe")];
        HashSet<string> refusedGroups = refused.Select(r => r.Match).Where(denominator.Contains).ToHashSet(StringComparer.Ordinal);
        HashSet<string> apiTry = ApiTryGroups(trials, propertyGroups, refused, writablePropertyGroups);
        var outside = new SortedDictionary<string, int>(apiTry.Where(g => !denominator.Contains(g))
            .GroupBy(g => status.TryGetValue(g, out string? reason) && reason.StartsWith("excluded", StringComparison.Ordinal) ? reason : "not_in_operation_layer")
            .ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);
        apiTry.IntersectWith(denominator);
        HashSet<string> tools = operations.Where(Implemented).Select(u => u.Key).ToHashSet(StringComparer.Ordinal);
        HashSet<string> callable = tools.Union(apiTry, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        Unit[] actions = [.. all.Where(u => u.Layer == ApiCatalog.Action && Counted(u))];
        HashSet<string> whitelist = actionWhitelist.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Trial(Unit u) => ActionTryPolicy.Name(u.Scope) && (u.Kind is null || ActionTryPolicy.Name(u.Kind))
            && !ActionTryPolicy.Denied("action", u.Scope + "/" + u.Kind) && !whitelist.Contains(u.Key)
            && !all.Any(other => other.Layer == ApiCatalog.Action && other.Exclusion is not null
                && string.Equals(other.Key, u.Key, StringComparison.OrdinalIgnoreCase));
        return new(new(denominator.Count, tools.Count, apiTry.Count, callable.Count, outside, refusedGroups.Count),
            new(actions.Length, actions.Count(Implemented), actions.Count(Trial)),
            [.. operations.GroupBy(u => u.Scope).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new NamespaceReachability(g.Key, g.Count(u => callable.Contains(u.Key)), g.Count(u => refusedGroups.Contains(u.Key))))]);
    }

    internal static HashSet<string> ApiTryGroups(string trials, IEnumerable<string> propertyGroups, IReadOnlyList<ApiRule>? refused = null, IEnumerable<string>? writablePropertyGroups = null) => trials.Split('\n')
        .Skip(1).Select(l => l.TrimEnd('\r').Split('\t')).Where(r => r.Length >= 3 && r[2] is "read" or "write")
        .Where(r => !Refused(r[0], r[2], refused) && !Refused(r[0], "call", refused))
        .Select(r => r[0]).Concat(propertyGroups.Where(g => !Refused(g, "read", refused)))
        .Concat((writablePropertyGroups ?? []).Where(g => !Refused(g, "write", refused))).ToHashSet(StringComparer.Ordinal);

    private static bool Refused(string group, string side, IReadOnlyList<ApiRule>? rules) => rules?.Any(r => r.Match == group && r.Sides?.Contains(side, StringComparer.Ordinal) == true) == true;

    internal static string[] ActionWhitelist()
    {
        // These are the same shipped entries used by ActionTryTools.Check; no installation/UI enumeration is needed.
        using Stream stream = typeof(ActionRunArgs).Assembly.GetManifestResourceStream("EplanMcp.Contracts.ActionRunWhitelist.json")!;
        using JsonDocument document = JsonDocument.Parse(stream);
        return [.. document.RootElement.GetProperty("entries").EnumerateArray().Select(e => "A:" + e.GetProperty("action").GetString()
            + (e.GetProperty("fixed").TryGetProperty("TYPE", out JsonElement type) ? "/" + type.GetString() : ""))];
    }

    internal string Headline(int liveVerified) => string.Create(CultureInfo.InvariantCulture,
        $"操作层 {Operation.Denominator} 组：可调用 {Operation.Callable}（工具 {Operation.Tools}，api_try {Operation.ApiTry}），实机验证 {liveVerified}");
}
