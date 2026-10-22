using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Knowledge;

/// <summary>
/// The content (<c>spec</c>) each kind of knowledge document may have when an agent writes it (docs/tools/
/// eplan_asset_update.md, section 2), checked strictly: unknown members, wrong types, over-long texts and references
/// that resolve to nothing are problems, each with its JSON pointer, all reported at once. EplanMcp adds the members only
/// it sets (a candidate is <c>proposed</c>, a gap <c>open</c>), so an agent never confirms its own inference.
/// </summary>
public static class KnowledgeSpecs
{
    public const int MaxText = 2000;

    public const int MaxShortText = 200;

    private static readonly Dictionary<KnowledgeKind, (string Member, bool Required, Shape Shape)[]> _members = new()
    {
        [KnowledgeKind.Candidate] = [("statement", true, Shape.Text), ("basis", true, Shape.Refs), ("confidence", true, Shape.Confidence), ("about", false, Shape.Refs)],
        [KnowledgeKind.Gap] = [("missing", true, Shape.Text), ("related", false, Shape.Refs)],
        [KnowledgeKind.Module] = [("macro", true, Shape.Macro), ("description", false, Shape.Text), ("parameters", false, Shape.Parameters)],
        [KnowledgeKind.MaterialMapping] = [("entries", true, Shape.Entries), ("source", false, Shape.Ref)],
    };

    private static readonly Dictionary<KnowledgeKind, (string Member, string Value)[]> _set = new()
    {
        [KnowledgeKind.Candidate] = [("status", "proposed"), ("inferredBy", "agent")],
        [KnowledgeKind.Gap] = [("status", "open")],
    };

    private enum Shape
    {
        Text,
        Ref,
        Refs,
        Macro,
        Confidence,
        Parameters,
        Entries,
    }

    /// <summary>
    /// The spec to store for <paramref name="spec"/> of <paramref name="kind"/>, references normalised by
    /// <paramref name="resolve"/> (null: it resolves to nothing; else the form to store and the asset's kind), and every
    /// problem as (JSON pointer below <c>/spec</c>, message).
    /// </summary>
    public static (JsonObject Spec, List<(string Pointer, string Message)> Problems) Check(KnowledgeKind kind, JsonObject spec, Func<string, (string Stored, string Kind)?> resolve)
    {
        var stored = new JsonObject();
        var problems = new List<(string, string)>();
        (string Member, bool Required, Shape Shape)[] members = _members[kind];
        foreach ((string name, JsonNode? value) in spec)
        {
            if (!members.Any(m => m.Member == name))
            {
                bool set = _set.GetValueOrDefault(kind)?.Any(s => s.Member == name) == true;
                problems.Add(($"/spec/{name}", set ? "is set by EplanMcp, never by an agent: only the engineer accepts, rejects or closes" : $"is not a member of a {Folder(kind)} spec; it has {string.Join(", ", members.Select(m => m.Member))}"));
            }
        }

        foreach ((string member, bool required, Shape shape) in members)
        {
            JsonNode? value = spec[member];
            if (value is null)
            {
                if (required)
                {
                    problems.Add(($"/spec/{member}", "is required"));
                }

                continue;
            }

            if (Value(value, shape, $"/spec/{member}", required, resolve, problems) is { } good)
            {
                stored[member] = good;
            }
        }

        foreach ((string member, string value) in _set.GetValueOrDefault(kind) ?? [])
        {
            stored[member] = value;
        }

        return (stored, problems);
    }

    /// <summary>A text of 1 to <paramref name="max"/> characters without control characters other than line feed and tab, or null.</summary>
    public static bool IsText(JsonNode? node, int max) =>
        node is JsonValue value && value.TryGetValue(out string? text) && text.Length is > 0 && text.Length <= max && !text.Any(c => char.IsControl(c) && c is not '\n' and not '\t');

    private static JsonNode? Value(JsonNode value, Shape shape, string pointer, bool required, Func<string, (string Stored, string Kind)?> resolve, List<(string, string)> problems)
    {
        switch (shape)
        {
            case Shape.Text:
                return IsText(value, MaxText) ? value.DeepClone() : Fail($"must be 1 to {MaxText} characters without control characters");
            case Shape.Confidence:
                return value is JsonValue v && v.TryGetValue(out string? level) && level is "low" or "medium" or "high" ? value.DeepClone() : Fail("must be low, medium or high");
            case Shape.Ref or Shape.Macro:
                (string Stored, string Kind)? found = value is JsonValue r && r.TryGetValue(out string? text) ? resolve(text) : null;
                return found is null ? Fail("must be an asset id from eplan_asset_search, or fact:<resource path>#<fact name> of this pack, that exists")
                    : shape == Shape.Macro && found.Value.Kind != "macro" ? Fail("must be a macro of this pack") : JsonValue.Create(found.Value.Stored);
            case Shape.Refs:
                return Array(value, required ? 1 : 0, 20, (item, at) => Value(item, Shape.Ref, at, true, resolve, problems));
            case Shape.Parameters:
                return Array(value, 0, 50, (item, at) => Object(item, at, problems, [
                    ("name", true, n => n is JsonValue x && x.TryGetValue(out string? s) && PackManifestValidator.IsId(s.Replace('_', '-'))),
                    ("type", true, n => n is JsonValue x && x.TryGetValue(out string? s) && s is "text" or "number" or "boolean" or "choice"),
                    ("unit", false, n => IsText(n, 20)),
                    ("values", false, n => n is JsonArray values && values.Count <= 50 && values.All(e => IsText(e, MaxShortText))),
                    ("placeholder", false, n => IsText(n, MaxShortText))]));
            default:
                return Array(value, 1, 500, (item, at) => Object(item, at, problems, [
                    ("when", true, n => n is JsonObject when && when.Count is > 0 and <= 20 && when.All(p => IsText(p.Value, MaxShortText))),
                    ("part", true, n => IsText(n, 100))]));
        }

        JsonNode? Fail(string message)
        {
            problems.Add((pointer, message));
            return null;
        }

        JsonNode? Array(JsonNode node, int min, int max, Func<JsonNode, string, JsonNode?> item)
        {
            if (node is not JsonArray array || array.Count < min || array.Count > max)
            {
                return Fail($"must be an array of {min} to {max} entries");
            }

            JsonNode?[] items = [.. array.Select((e, i) => e is null ? Fail("has a null entry") : item(e, $"{pointer}/{i}"))];
            return items.All(i => i is not null) ? new JsonArray(items) : null;
        }
    }

    private static JsonNode? Object(JsonNode node, string pointer, List<(string, string)> problems, (string Member, bool Required, Func<JsonNode, bool> Valid)[] members)
    {
        int before = problems.Count;
        if (node is not JsonObject item)
        {
            problems.Add((pointer, $"must be an object with {string.Join(", ", members.Select(m => m.Member))}"));
            return null;
        }

        problems.AddRange(item.Where(p => !members.Any(m => m.Member == p.Key)).Select(p => ($"{pointer}/{p.Key}", "is not a member here")));
        foreach ((string member, bool required, Func<JsonNode, bool> valid) in members)
        {
            if (item[member] is { } value ? !valid(value) : required)
            {
                problems.Add(($"{pointer}/{member}", item[member] is null ? "is required" : "has a value of the wrong type, length or form; eplan_tool_usage shows the form"));
            }
        }

        return problems.Count == before ? item.DeepClone() : null;
    }

    private static string Folder(KnowledgeKind kind) => KnowledgeDocument.Folder(kind);
}
