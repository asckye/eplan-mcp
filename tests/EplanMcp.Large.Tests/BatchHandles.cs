using System.Runtime.Versioning;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Receivers and arguments the discovery recipes cannot name: an object reached only as a session handle (ADR-0033).
/// One read call on a producer of that type supplies it, so a group whose receiver is an intermediate object is
/// trialled instead of being recorded as needing context. Only reads produce a handle: a trial never writes to obtain
/// a receiver, and only a producer whose own receiver is a discovery locator is used, so the chain stays one step.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BatchHandles
{
    /// <summary>The read groups that return <paramref name="type"/> from a discoverable receiver, cheapest first.</summary>
    internal static string[] Producers(string type) =>
    [
        .. ApiTryCatalog.Producers(type).Where(Readable)
            .Where(group => BatchValues.ObjectKinds(group) is { Length: > 0 })
            .OrderBy(group => ApiTryCatalog.Shape(group)?.Length ?? int.MaxValue)
            .ThenBy(group => group, StringComparer.Ordinal),
    ];

    /// <summary>A read that changes nothing: a property getter, or a read method without arguments.</summary>
    private static bool Readable(string group)
    {
        if (!ApiTryCatalog.Allowed(group) || ApiTryCatalog.Refused(group, "read") is not null
            || ApiTryLifecycle.Effect(group) is not null || ApiTryCatalog.Settings(group))
        { return false; }
        string? access = ApiTryCatalog.MethodAccess(group);
        return access is null or "read" && ApiTryCatalog.Refused(group, "call") is null
            && ApiTryCatalog.Shape(group) is { Length: 2 };
    }

    /// <summary>The first session handle in a trial result, preferring the wanted type over a subclass or a sibling.</summary>
    internal static JsonElement? Handle(JsonElement value, string type)
    {
        List<JsonElement> handles = [];
        Collect(value, handles);
        foreach (JsonElement handle in handles)
        {
            if (handle.GetProperty("type").GetString() == type)
            { return handle.Clone(); }
        }
        return handles.Count > 0 ? handles[0].Clone() : null;
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
