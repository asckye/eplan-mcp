#if COVERAGE_TESTS
extern alias host;
using host::EplanMcp.Host.Tools;
#else
using EplanMcp.Host.Tools;
#endif
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[SupportedOSPlatform("windows")]
internal sealed record BatchEntry(string Id, string Kind, string[]? ObjectKinds = null, string[]? ValueShape = null, Dictionary<string, string[]>? Parameters = null, bool Method = false, string? Signature = null, ActionCatalogItem? Action = null)
{
    internal string Group => Id.EndsWith("#write", StringComparison.Ordinal) ? Id[..^6] : Id;
    internal bool Destructive => ApiTryLifecycle.Effect(Group) == "removes";
    internal bool NeedsContext => Action is { } action && (action.Context?.Any(c => c is "file" or "page" or "selection") == true
        || ActionCallTemplates.Example(action).Any(p => p.Value?.GetValue<string>().StartsWith("<scratch ", StringComparison.Ordinal) == true)
        || action.Parameters.Any(p => p.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True
            && !ActionCallTemplates.Example(action).ContainsKey(p.GetProperty("name").GetString()!)))
        || ObjectKinds is { Length: 0 } || Kind == "api-write" && Group.EndsWith(".FileName", StringComparison.Ordinal)
        || Parameters?.Any(p => Signature is not null && ApiTryCatalog.PathDirection(Signature, p.Key) is not null
        || p.Value[0] is "handle" or "via" or "Eplan.EplApi.DataModel.Page") == true;
}
internal sealed class BatchObservation(string id, string kind)
{
    public string Id { get; } = id;
    public string Kind { get; } = kind;
    public string Outcome { get; set; } = "failed";
    public double Seconds { get; set; }
    public bool Changed { get; set; }
    [JsonPropertyName("undo_restores")]
    public bool? UndoRestores { get; set; }
    [JsonPropertyName("eplan_messages")]
    public int EplanMessages { get; set; }
    public string? Detail { get; set; }
}

[SupportedOSPlatform("windows")]
internal static class BatchValues
{
    internal static BatchEntry[] Entries(IEnumerable<ActionCatalogItem> actions) => [.. ApiEntries(), .. actions.Where(a => a.Source == "official")
        .DistinctBy(a => (a.Name, a.Type)).OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.Type, StringComparer.Ordinal)
        .Select(a => new BatchEntry("A:" + a.Name + (a.Type is null ? "" : "/" + a.Type), "action", Action: a))];
    internal static Dictionary<string, int> ExpectedCounts(BatchEntry[] entries) => new(StringComparer.Ordinal)
    {
        ["api-read"] = entries.Count(e => e.Kind == "api-read"),
        ["api-write"] = entries.Count(e => e.Kind == "api-write"),
        ["action"] = entries.Count(e => e.Kind == "action"),
    };

    internal static BatchEntry[] ApiEntries()
    {
        var entries = new List<BatchEntry>();
        // Property groups dispatched as methods are trialled in the method loop; indexed chain properties (read-only, a
        // handle-method access but no MethodGroups row) stay here and need a handle context.
        foreach (string group in ApiTryCatalog.PropertyGroups.Where(g => !ApiTryCatalog.MethodGroups.Contains(g, StringComparer.Ordinal)).Order(StringComparer.Ordinal))
        {
            string[]? kinds = ObjectKinds(group);
            entries.Add(new(group, "api-read", kinds));
            if (ApiTryCatalog.ValueShape(group) is { } shape)
            { entries.Add(new(group + "#write", "api-write", kinds, shape)); }
        }
        foreach (string group in ApiTryCatalog.MethodGroups)
        {
            // Trial one overload per group, preferring the fewest required arguments.
            string[] method = ApiTryCatalog.Methods(group).OrderBy(m => m.Length).ThenBy(m => m[1], StringComparer.Ordinal).First();
            bool write = ApiTryCatalog.MethodAccess(group) == "write";
            entries.Add(new(group + (write ? "#write" : ""), write ? "api-write" : "api-read", ReceiverKinds(method[1]),
                Parameters: method.Skip(2).ToDictionary(n => n, n => ApiTryCatalog.ParameterShape(method[1], n)!, StringComparer.Ordinal), Method: true, Signature: method[1]));
        }
        return [.. entries];
    }
    // Static and factory calls omit target; services retain main's Project locator anchor.
    internal static string[]? ObjectKinds(string group) => ApiTryCatalog.Shape(group) is { } shape ? ReceiverKinds(shape[1]) : [];
    private static string[]? ReceiverKinds(string signature) => ApiTryCatalog.ReceiverShape(signature) is { } shape
        ? shape[0] is "handle" or "via" ? [] : DiscoveryKinds(ApiTryCatalog.Kinds(shape[0])) : null;
    private static string[] DiscoveryKinds(string[] kinds) => [.. ObjectReadTypes.Types.Where(ObjectReadTypes.Supported)
        .Where(name => kinds.Contains(name, StringComparer.OrdinalIgnoreCase))];

    internal static async Task<Dictionary<string, object?>?> ArgumentsAsync(BatchEntry entry, Func<string[], Task<JsonElement?>> find)
    {
        if (entry.NeedsContext)
        { return null; }
        if (entry.Action is { } action)
        { return ActionCallTemplates.Example(action).ToDictionary(p => p.Key, p => (object?)p.Value?.GetValue<string>(), StringComparer.Ordinal); }
        if (entry.Signature is not null)
        {
            try
            { ApiTryPreconditions.Check(entry.Group, ["", entry.Signature, .. entry.Parameters!.Keys], _ => false); }
            catch (BridgeErrorException)
            { return null; }
        }
        var parameters = new Dictionary<string, object?>();
        foreach ((string parameter, string[] shape) in entry.Parameters ?? [])
        {
            bool array = shape[0] == "array";
            string[] element = array ? shape[1..] : shape;
            string[] kinds = DiscoveryKinds(ApiTryCatalog.Kinds(element[0]));
            // A page/handle/file needs a caller-provided context; never pick a page or fabricate a source call.
            object? value = element[0] == "Eplan.EplApi.DataModel.Page" ? null : kinds.Length > 0 ? await find(kinds) : Argument(element);
            if (value is null)
            { return null; }
            parameters[parameter] = array ? new[] { value } : value;
        }
        return parameters;
    }

    // Strings and coordinates require fixture knowledge. Boolean/integer arguments require an observed value.
    internal static object? Argument(string[] shape, string? current = null) => shape[0] switch
    {
        "enum" => shape.ElementAtOrDefault(1),
        "System.Boolean" when bool.TryParse(current, out bool value) => value,
        "System.Int32" when int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) => value,
        "System.Int64" when long.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) => value,
        _ => null,
    };
    internal static object? Next(string type, string? value, string[]? members = null) => value is null ? null : type switch
    {
        "System.Boolean" => !bool.Parse(value),
        "System.Int32" => checked(int.Parse(value, CultureInfo.InvariantCulture) + 1),
        "System.Int64" => checked(long.Parse(value, CultureInfo.InvariantCulture) + 1),
        "System.Double" => double.Parse(value, CultureInfo.InvariantCulture) + 0.5,
        "System.String" => value + "_v",
        _ => members is { Length: > 1 } && Array.IndexOf(members, value) is >= 0 and var index ? members[(index + 1) % members.Length] : null,
    };
    internal static bool Stops(BatchObservation result) => result.Outcome is "outcome_unknown" or "modal" or "timeout" || result.UndoRestores == false || result.Changed && (result.Kind == "api-read" || result.Outcome == "rolled_back");
    internal static void Classify(BatchObservation result)
    {
        if (result.Outcome == "rolled_back" && result.EplanMessages == 0
            && result.Detail?.Contains("EPLAN could not complete", StringComparison.Ordinal) == true)
        { result.Outcome = "failed:needs_context"; }
    }
    internal static BatchEntry[] Resume(BatchEntry[] entries, string? from, string? after = null)
    {
        if (from is not null && after is not null)
        { throw new ArgumentException("Set only one of EPLANMCP_BATCH_FROM and EPLANMCP_BATCH_AFTER."); }
        string? id = from ?? after;
        int index = id is null ? 0 : Array.FindIndex(entries, e => e.Id == id);
        return index < 0 ? throw new ArgumentException((after is null ? "EPLANMCP_BATCH_FROM" : "EPLANMCP_BATCH_AFTER") + " is not an entry ID: " + id)
            : entries[(index + (after is null ? 0 : 1))..];
    }
}
