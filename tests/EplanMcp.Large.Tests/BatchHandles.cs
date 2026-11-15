using System.Runtime.Versioning;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Receivers and arguments the discovery recipes cannot name: an object reached only as a session handle (ADR-0033).
/// One read call on a producer of that type supplies it, so a group whose receiver is an intermediate object is
/// trialled instead of being recorded as needing context. Only reads produce a handle: a trial never writes to obtain
/// a receiver. A producer's own receiver is a discovery locator or, up to <see cref="Depth"/> steps, another handle:
/// the parts database's template positions are reached as project, OpenDatabase, Parts, then the part's positions.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BatchHandles
{
    /// <summary>How many producer steps a receiver may be away from a discovery locator.</summary>
    internal const int Depth = 4;

    /// <summary>
    /// The read groups that return <paramref name="type"/> from a discoverable receiver or, within <paramref name="depth"/>
    /// steps, from a receiver that is itself produced this way; those one step away first, then the cheapest.
    /// </summary>
    internal static string[] Producers(string type, int depth = Depth) =>
    [
        .. Declared(type).Where(Readable)
            .Where(group => Steps(group, depth) is not null)
            .OrderBy(group => Steps(group, depth))
            .ThenBy(group => ApiTryCatalog.Shape(group)?.Length ?? int.MaxValue)
            .ThenBy(group => group, StringComparer.Ordinal),
    ];

    /// <summary>1 when the producer's receiver is a discovery locator, n when it is a handle n - 1 steps away; null when none.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Group, int Depth), int?> _steps = new();

    private static int? Steps(string group, int depth) => _steps.GetOrAdd((group, depth), key => CountSteps(key.Group, key.Depth));

    private static int? CountSteps(string group, int depth)
    {
        if (depth <= 0)
        { return null; }
        if (ReceiverKinds(group) is { Length: > 0 })
        { return 1; }
        if (ReceiverKinds(group) is not { Length: 0 } || Owner(group) is not { } receiver)
        { return null; }
        int? best = null;
        foreach (string producer in Declared(receiver).Where(Readable).Where(p => p != group))
        {
            if (Steps(producer, depth - 1) is int steps && (best is null || steps + 1 < best))
            { best = steps + 1; }
        }
        return best;
    }

    /// <summary>
    /// The groups declared to return <paramref name="type"/> or one of its base classes: EPLAN hands out a subclass where
    /// the signature names the base (A137), so the terminal template positions come from MDPart.FunctionTemplatePositions.
    /// Which handle is really the wanted subclass shows only in the result (<see cref="Handle"/> with exact).
    /// </summary>
    internal static IEnumerable<string> Declared(string type)
    {
        for (string? current = type; current is not null; current = _bases.Value.GetValueOrDefault(current))
        {
            foreach (string group in ApiTryCatalog.Producers(current))
            { yield return group; }
        }
    }

    /// <summary>Each EPLAN type's base class, from the committed catalog (catalog/eplan-2026.0.3/types.jsonl).</summary>
    private static readonly Lazy<Dictionary<string, string>> _bases = new(() =>
    {
        var bases = new Dictionary<string, string>(StringComparer.Ordinal);
        using Stream stream = typeof(BatchHandles).Assembly.GetManifestResourceStream("CatalogTypes.jsonl")!;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            using JsonDocument row = JsonDocument.Parse(line);
            if (row.RootElement.TryGetProperty("base", out JsonElement parent) && parent.GetString() is { } name && name.StartsWith("Eplan.", StringComparison.Ordinal)
                && row.RootElement.GetProperty("id").GetString() is ['T', ':', .. var id])
            { bases[id] = name; }
        }
        return bases;
    });

    /// <summary>A read that changes nothing: a property getter, or a read method without arguments.</summary>
    private static bool Readable(string group)
    {
        if (!ApiTryCatalog.Allowed(group) || ApiTryCatalog.Refused(group, "read") is not null
            || ApiTryLifecycle.Effect(group) is not null || ApiTryCatalog.Settings(group))
        { return false; }
        string? access = ApiTryCatalog.MethodAccess(group);
        return access is null or "read" && ApiTryCatalog.Refused(group, "call") is null && Signature(group) is not null;
    }

    /// <summary>
    /// The call a producer read makes: the property getter or method without arguments, or, for a method with several
    /// overloads (MDPartsManagement.OpenDatabase), its overload without arguments.
    /// </summary>
    internal static string? Signature(string group) => ApiTryCatalog.Shape(group) is { Length: 2 } shape ? shape[1]
        : ApiTryCatalog.Shape(group) is null ? ApiTryCatalog.Methods(group).Where(m => m.Length == 2).Select(m => m[1]).SingleOrDefault() : null;

    /// <summary>The type that declares <paramref name="group"/>, whose instance the read is made on.</summary>
    internal static string? Owner(string group) => ApiTryCatalog.Shape(group)?[0] ?? ApiTryCatalog.Methods(group).FirstOrDefault()?[0];

    /// <summary>The discovery kinds of a producer's receiver; empty for a handle receiver, null when there is none.</summary>
    internal static string[]? ReceiverKinds(string group) => Signature(group) is { } signature ? BatchValues.ReceiverKinds(signature) : null;

    /// <summary>
    /// The first session handle in a trial result, preferring the wanted type over a subclass or a sibling; with
    /// <paramref name="exact"/>, only one of the wanted type (a producer declared for a base class).
    /// </summary>
    internal static JsonElement? Handle(JsonElement value, string type, bool exact = false)
    {
        List<JsonElement> handles = [];
        Collect(value, handles);
        foreach (JsonElement handle in handles)
        {
            if (handle.GetProperty("type").GetString() == type)
            { return handle.Clone(); }
        }
        return handles.Count > 0 && !exact ? handles[0].Clone() : null;
    }

    private static void Collect(JsonElement value, List<JsonElement> handles)
    {
        if (handles.Count > 16)
        { return; }
        switch (value.ValueKind)
        {
            case JsonValueKind.Object when value.TryGetProperty("handle", out JsonElement token) && token.ValueKind == JsonValueKind.String
                && value.TryGetProperty("type", out JsonElement kind) && kind.ValueKind == JsonValueKind.String:
                handles.Add(value);
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in value.EnumerateObject())
                { Collect(property.Value, handles); }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray())
                { Collect(item, handles); }
                break;
            default:
                break;
        }
    }
}
