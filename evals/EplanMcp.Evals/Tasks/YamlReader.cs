using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace EplanMcp.Evals.Tasks;

/// <summary>
/// Reads values out of a YAML document and collects every problem with its line, instead of stopping at the first.
/// Each accessor returns null after reporting, so callers go on checking the rest of the file.
/// </summary>
internal partial class YamlReader
{
    // Aliases repeat nodes, so a small file can expand without bound; real task files stay far below this.
    private const int MaxJsonNodes = 10_000;

    private readonly HashSet<YamlNode> _converting = new(ReferenceEqualityComparer.Instance);
    private int _jsonNodes;

    public List<string> Problems { get; } = [];

    public static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.FirstOrDefault(c => c.Key is YamlScalarNode { Value: { } k } && k == key).Value;

    public static bool IsString(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String;

    public YamlNode? Required(YamlMappingNode map, string key) => Child(map, key) ?? Reported<YamlNode>(map, $"'{key}' is missing");

    /// <summary>A non-blank scalar; <paramref name="subject"/> names it in the problem, for example <c>'id'</c>.</summary>
    public string? Text(YamlNode? node, string subject) => node switch
    {
        null => null,
        YamlScalarNode { Value: { } text } when !string.IsNullOrWhiteSpace(text) => text,
        _ => Reported<string>(node, $"{subject} must be a non-empty string"),
    };

    public YamlMappingNode? Mapping(YamlNode node, string subject) =>
        node as YamlMappingNode ?? Reported<YamlMappingNode>(node, $"{subject} must be a mapping");

    public void Unknown(YamlMappingNode map, string[] known)
    {
        foreach (YamlNode key in map.Children.Keys.Where(k => k is not YamlScalarNode { Value: { } name } || !known.Contains(name, StringComparer.Ordinal)))
        {
            Problem(key, $"unknown key {(key as YamlScalarNode)?.Value ?? "(not a string)"}; expected {string.Join(", ", known)}");
        }
    }

    /// <summary>
    /// YAML to JSON. Quoted scalars are strings; plain ones are null (<c>null</c>, <c>~</c>, empty), booleans, JSON
    /// numbers (<c>007</c> and <c>1.</c> are not) or strings. Tags such as <c>!!str</c> are refused rather than half
    /// supported: quoting says the same. An alias inside the node it refers to has no JSON form and is reported.
    /// </summary>
    public JsonNode? Json(YamlNode node)
    {
        if (!node.Tag.IsEmpty && !node.Tag.IsNonSpecific)
        {
            return Reported<JsonNode>(node, $"YAML tags such as {node.Tag.Value} are not supported; quote the value instead");
        }

        if (++_jsonNodes > MaxJsonNodes)
        {
            return _jsonNodes == MaxJsonNodes + 1 ? Reported<JsonNode>(node, $"the file expands to more than {MaxJsonNodes} values") : null;
        }

        if (!_converting.Add(node))
        {
            return Reported<JsonNode>(node, "an alias refers to a node that contains it");
        }

        try
        {
            return Convert(node);
        }
        finally
        {
            _converting.Remove(node);
        }
    }

    public void Problem(YamlNode node, string message) => Problems.Add($"line {node.Start.Line}: {message}");

    /// <summary>Reports a problem and returns null, so an accessor can report in an expression.</summary>
    public T? Reported<T>(YamlNode node, string message)
        where T : class
    {
        Problem(node, message);
        return null;
    }

    private JsonNode? Convert(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
                var json = new JsonObject();
                foreach ((YamlNode key, YamlNode value) in map.Children)
                {
                    if (key is YamlScalarNode { Value: { } name })
                    {
                        json[name] = Json(value);
                    }
                    else
                    {
                        Problem(key, "keys must be strings");
                    }
                }

                return json;
            case YamlSequenceNode sequence:
                return new JsonArray([.. sequence.Children.Select(Json)]);
            case YamlScalarNode { Style: not YamlDotNet.Core.ScalarStyle.Plain } quoted:
                return JsonValue.Create(quoted.Value ?? string.Empty);
            case YamlScalarNode { Value: var text }:
                return text switch
                {
                    null or "" or "~" or "null" or "Null" or "NULL" => null,
                    "true" or "True" or "TRUE" => JsonValue.Create(true),
                    "false" or "False" or "FALSE" => JsonValue.Create(false),
                    _ when JsonNumber().IsMatch(text) => JsonNode.Parse(text),
                    _ => JsonValue.Create(text),
                };
            default:
                return Reported<JsonNode>(node, "unsupported YAML node");
        }
    }

    [GeneratedRegex(@"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][-+]?[0-9]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex JsonNumber();
}
