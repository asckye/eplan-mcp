using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EplanMcp.Evals.Scoring;

/// <summary>
/// The JSONPath subset of the task files (agent evals design 4.1): <c>$</c>, <c>.field</c>, <c>[n]</c> and <c>[*]</c>.
/// A path without <c>[*]</c> selects one value or nothing; a path with it selects the array of every match, which may
/// be empty.
/// </summary>
internal sealed partial class JsonPath
{
    private readonly Segment[] _segments;

    private JsonPath(string text, Segment[] segments)
    {
        Text = text;
        _segments = segments;
    }

    public string Text { get; }

    /// <summary>The field the path starts with, or null for <c>$</c> and paths that start with an index.</summary>
    public string? FirstField => _segments is [{ Field: { } name }, ..] ? name : null;

    /// <summary>True when the path selects an array of every match rather than one value.</summary>
    public bool HasWildcard => _segments.Any(s => s is { Field: null, Index: null });

    public static bool TryParse(string text, [NotNullWhen(true)] out JsonPath? path)
    {
        Match match = Syntax().Match(text);
        if (!match.Success)
        {
            path = null;
            return false;
        }

        path = new JsonPath(text, [.. match.Groups["segment"].Captures.Select(c => c.Value switch
        {
            ['.', .. string name] => new Segment(name, null),
            "[*]" => new Segment(null, null),
            ['[', .. string index, ']'] => new Segment(null, int.Parse(index, NumberStyles.None, CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException("The syntax admits no other segment."),
        })]);
        return true;
    }

    /// <summary>False when a path without <c>[*]</c> matches nothing; a JSON null that is there is a match.</summary>
    public bool TrySelect(JsonNode? root, out JsonNode? value)
    {
        List<JsonNode?> matches = [root];
        foreach (Segment segment in _segments)
        {
            var next = new List<JsonNode?>();
            foreach (JsonNode? node in matches)
            {
                segment.Step(node, next);
            }

            matches = next;
        }

        if (HasWildcard)
        {
            value = new JsonArray([.. matches.Select(m => m?.DeepClone())]);
            return true;
        }

        value = matches.FirstOrDefault();
        return matches.Count == 1;
    }

    public override string ToString() => Text;

    // At most nine digits, so every index fits in an int; \z, because $ also matches before a final newline.
    [GeneratedRegex(@"^\$(?<segment>\.[A-Za-z_][A-Za-z0-9_]*|\[(?:[0-9]{1,9}|\*)\])*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Syntax();

    /// <summary><c>.field</c> when <see cref="Field"/> is set, <c>[n]</c> when <see cref="Index"/> is, <c>[*]</c> otherwise.</summary>
    private readonly record struct Segment(string? Field, int? Index)
    {
        public void Step(JsonNode? node, List<JsonNode?> into)
        {
            switch (this, node)
            {
                case ({ Field: { } name }, JsonObject parent) when parent.TryGetPropertyValue(name, out JsonNode? child):
                    into.Add(child);
                    break;
                case ({ Index: { } index }, JsonArray array) when index < array.Count:
                    into.Add(array[index]);
                    break;
                case ({ Field: null, Index: null }, JsonArray array):
                    into.AddRange(array);
                    break;
            }
        }
    }
}
