using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Evals.Scoring;

/// <summary>How a check compares the answer with the expected value (agent evals design 4.1).</summary>
internal enum ComparisonKind
{
    /// <summary><c>equals</c>: numbers by value, strings ordinally, arrays and objects element by element.</summary>
    Equal,

    /// <summary><c>equals_ignore_case</c>: two strings, ignoring case.</summary>
    EqualIgnoringCase,

    /// <summary><c>set_equals</c>: two arrays with the same elements, in any order and with duplicates ignored.</summary>
    SetEqual,

    /// <summary><c>contains</c>: an array with the expected element (or every element of an expected array), or a string with the expected substring.</summary>
    Contains,

    /// <summary><c>at_least</c>: a number not below the expected number.</summary>
    AtLeast,

    /// <summary><c>at_most</c>: a number not above the expected number.</summary>
    AtMost,
}

internal static class Comparison
{
    /// <summary>The comparison names of the task files.</summary>
    public static IReadOnlyDictionary<string, ComparisonKind> Names { get; } = new Dictionary<string, ComparisonKind>(StringComparer.Ordinal)
    {
        ["equals"] = ComparisonKind.Equal,
        ["equals_ignore_case"] = ComparisonKind.EqualIgnoringCase,
        ["set_equals"] = ComparisonKind.SetEqual,
        ["contains"] = ComparisonKind.Contains,
        ["at_least"] = ComparisonKind.AtLeast,
        ["at_most"] = ComparisonKind.AtMost,
    };

    private const int MaxShown = 5;

    // Keep Chinese text readable in the report instead of \u escapes.
    private static readonly JsonSerializerOptions _readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Null when <paramref name="actual"/> passes, otherwise why it does not.</summary>
    public static string? Fail(ComparisonKind kind, JsonNode? actual, JsonNode? expected) => kind switch
    {
        ComparisonKind.Equal => Same(actual, expected) ? null : $"{Show(actual)} is not {Show(expected)}",
        ComparisonKind.EqualIgnoringCase => (Text(actual), Text(expected)) switch
        {
            ({ } a, { } e) => string.Equals(a, e, StringComparison.OrdinalIgnoreCase) ? null : $"{Show(actual)} is not {Show(expected)}, ignoring case",
            _ => $"{Show(actual)} and {Show(expected)} are not both strings",
        },
        ComparisonKind.SetEqual => (actual, expected) switch
        {
            (JsonArray a, JsonArray e) => Differences(Missing(e, a), Missing(a, e)),
            _ => $"{Show(actual)} and {Show(expected)} are not both arrays",
        },
        ComparisonKind.Contains => (actual, Text(actual), Text(expected)) switch
        {
            (JsonArray a, _, _) => Differences(Missing(expected is JsonArray e ? e : [expected?.DeepClone()], a), []),
            (_, { } a, { } e) => a.Contains(e, StringComparison.Ordinal) ? null : $"{Show(actual)} does not contain {Show(expected)}",
            _ => $"{Show(actual)} is neither an array nor a string that can contain {Show(expected)}",
        },
        ComparisonKind.AtLeast or ComparisonKind.AtMost => (Number(actual), Number(expected)) switch
        {
            ({ } a, { } e) when kind == ComparisonKind.AtLeast => a >= e ? null : $"{Show(actual)} is below {Show(expected)}",
            ({ } a, { } e) => a <= e ? null : $"{Show(actual)} is above {Show(expected)}",
            _ => $"{Show(actual)} and {Show(expected)} are not both numbers",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Why <paramref name="kind"/> cannot compare with <paramref name="expected"/>, or null when it can: checked for the
    /// literals of a task file when it is loaded, and for oracle values when they are known.
    /// </summary>
    public static string? ExpectedProblem(ComparisonKind kind, JsonNode? expected) => kind switch
    {
        ComparisonKind.AtLeast or ComparisonKind.AtMost when Number(expected) is null => "compares with a number",
        ComparisonKind.EqualIgnoringCase when Text(expected) is null => "compares with a string",
        ComparisonKind.SetEqual when expected is not JsonArray => "compares with a list",
        ComparisonKind.Contains when expected is JsonArray { Count: 0 } => "needs something to look for, not an empty list",
        _ => null,
    };

    /// <summary>The value of a JSON number; null for anything else and for numbers out of the decimal range.</summary>
    public static decimal? Number(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            && decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)
            ? number
            : null;

    /// <summary>Deep equality in which numbers compare by value (<c>1</c> equals <c>1.0</c>).</summary>
    public static bool Same(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (null, null) => true,
        (JsonObject x, JsonObject y) => x.Count == y.Count && x.All(p => y.TryGetPropertyValue(p.Key, out JsonNode? v) && Same(p.Value, v)),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y).All(p => Same(p.First, p.Second)),
        (JsonValue x, JsonValue y) when Number(x) is { } m && Number(y) is { } n => m == n,
        (JsonValue x, JsonValue y) when Text(x) is { } s && Text(y) is { } t => s == t,
        (JsonValue x, JsonValue y) => x.GetValueKind() == y.GetValueKind() && x.ToJsonString() == y.ToJsonString(),
        _ => false,
    };

    /// <summary>The value of a JSON string; null for anything else.</summary>
    public static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>The elements of <paramref name="wanted"/> that <paramref name="present"/> lacks, each once.</summary>
    private static List<JsonNode?> Missing(JsonArray wanted, JsonArray present)
    {
        var missing = new List<JsonNode?>();
        foreach (JsonNode? element in wanted)
        {
            if (!present.Any(p => Same(p, element)) && !missing.Any(m => Same(m, element)))
            {
                missing.Add(element);
            }
        }

        return missing;
    }

    private static string? Differences(List<JsonNode?> missing, List<JsonNode?> unexpected) =>
        (missing.Count, unexpected.Count) switch
        {
            (0, 0) => null,
            (_, 0) => $"missing {List(missing)}",
            (0, _) => $"unexpected {List(unexpected)}",
            _ => $"missing {List(missing)}; unexpected {List(unexpected)}",
        };

    private static string List(List<JsonNode?> nodes) =>
        string.Join(", ", nodes.Take(MaxShown).Select(Show)) + (nodes.Count > MaxShown ? $" and {nodes.Count - MaxShown} more" : string.Empty);

    internal static string Show(JsonNode? node) => node?.ToJsonString(_readable) ?? "null";
}
