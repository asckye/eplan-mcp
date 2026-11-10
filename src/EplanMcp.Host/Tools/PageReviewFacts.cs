using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tools;

/// <summary>Only values read from the reviewed page are facts: an empty property was read and is known absent; relations stay unknown.</summary>
internal sealed class PageReviewFacts : IRuleFactSnapshot
{
    private static readonly JsonElement _absent = JsonSerializer.SerializeToElement<string?>(null);
    private readonly Dictionary<string, (string Kind, JsonObject Row)> _objects = new(StringComparer.Ordinal);

    // Connection locator -> designation of the cable whose core it is, from the cables read on the page.
    private readonly Dictionary<string, string> _cables = new(StringComparer.Ordinal);

    internal PageReviewFacts(JsonObject actual, JsonObject page)
    {
        Add("page", page);
        foreach ((string field, string kind) in new[] { ("devices", "function"), ("connections", "connection") })
        {
            foreach (JsonObject row in (actual[field] as JsonArray ?? []).OfType<JsonObject>())
            { Add(kind, row); }
        }
        foreach (JsonObject cable in (actual["cables"] as JsonArray ?? []).OfType<JsonObject>())
        {
            foreach (JsonObject core in (cable["cores"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (core["locator"] is JsonObject locator && Text(cable["designation"]) is { Length: > 0 } designation)
                { _cables.TryAdd(locator.ToJsonString(), designation); }
            }
        }
    }

    private void Add(string kind, JsonObject row)
    {
        if (row["locator"] is JsonObject locator)
        { _objects.TryAdd(locator.ToJsonString(), (kind, row.DeepClone().AsObject())); }
    }

    public IReadOnlyList<string> Objects(string kind) => [.. _objects.Where(item => item.Value.Kind == kind).Select(item => item.Key)];

    internal JsonObject? GetObject(string locator) => _objects.TryGetValue(locator, out var value) ? value.Row : null;

    public RuleValue? GetProperty(string locator, string name)
    {
        if (!_objects.TryGetValue(locator, out var value) || !name.StartsWith(value.Kind + ".", StringComparison.Ordinal)
            || !RuleValidator.Properties.TryGetValue(name, out var property))
        { return null; }
        JsonObject row = value.Row;
        JsonNode? node = name switch
        {
            "page.name" => row["name"],
            "page.type" => row["type"],
            "function.name" => row["tag"],
            "function.category" => row["category"],
            "function.main" => row["main"],
            "function.letters" => JsonValue.Create(FunctionDesignation.Letters(Text(row["tag"]))),
            "connection.name" => row["name"],
            "connection.colour" => row["colour"],
            "connection.cross_section" => row["crossSection"],
            "connection.potential_name" => row["potentialName"],
            "connection.potential_type" => row["potentialType"],
            "connection.label" => row["label"],
            "connection.pins" => JsonValue.Create(string.Join("|", new[] { row["from"], row["to"] }.Select(end => Text(end?["pin"])).Where(pin => !string.IsNullOrEmpty(pin)))),
            // device:pin of both ends, so rules see PE terminals (-XD1:PE), earthing strips (-XE) and the device behind a pin.
            // Owner sign-off 2026-11-10: the functional colour rules skip cores of multicore cables (colours are the cable's).
            "connection.cable" => _cables.TryGetValue(locator, out string? cable) ? JsonValue.Create(cable) : null,
            "connection.ends" => JsonValue.Create(string.Join("|", new[] { row["from"], row["to"] }.Select(end => Text(end?["device"]) + ":" + Text(end?["pin"])).Where(end => end != ":"))),
            _ => null,
        };
        if (node is null || property.Type.Kind == "text" && string.IsNullOrEmpty(Text(node)))
        { return new(property.Type, _absent); }
        return new(property.Type, JsonSerializer.SerializeToElement(node));
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    public RuleRelation Relation(string locator, string relation) => new([], false, "Local snapshot does not prove complete endpoint relations.");
}
