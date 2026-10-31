using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// Examples and tool names in model-facing text match the registered tools (mcp-tool-design 10): every example in a
/// tool's description and in the Examples section of its usage file is a valid argument object for that tool, and no
/// text names an <c>eplan_*</c> tool that is not registered. That every tool has a usage file is checked in
/// <see cref="ToolContractTests"/>.
/// </summary>
[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed partial class ToolUsageExampleTests : IDisposable
{
    /// <summary>Tools named before they are registered. Remove the entry when the tool is registered.</summary>
    private static readonly (string Tool, string Reason)[] _planned = [];

    private static readonly JsonSerializerOptions _readableJson = new(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ServiceProvider _services;
    private readonly McpServerOptions _options;

    public ToolUsageExampleTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()) with { ActionRun = ActionRunSwitch.On, ApiTry = ActionRunSwitch.On, ActionTry = ActionRunSwitch.On });
        services.AddEplanMcp();
        _services = services.BuildServiceProvider();
        _options = _services.GetRequiredService<IOptions<McpServerOptions>>().Value;
    }

    public void Dispose() => _services.Dispose();

    private Tool[] Tools => [.. _options.ToolCollection!.Select(t => t.ProtocolTool)];

    [Fact]
    public void EveryExample_IsValidForItsTool()
    {
        var problems = new List<string>();
        foreach (Tool tool in Tools)
        {
            string[] usage = [.. CodeBlocks(ToolUsageCatalog.Usage[tool.Name][ToolUsageCatalog.Examples])];
            if (usage.Length == 0)
            {
                problems.Add($"{tool.Name}: the usage Examples section has no indented or fenced example.");
            }

            foreach (string example in DescriptionExample(tool.Description!).Concat(usage))
            {
                try
                {
                    using JsonDocument arguments = JsonDocument.Parse(example);
                    problems.AddRange(Violations(tool.InputSchema, arguments.RootElement, null).Select(v => $"{tool.Name} example {example}: {v}"));
                }
                catch (JsonException ex)
                {
                    problems.Add($"{tool.Name} example {example}: not JSON ({ex.Message})");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ModelFacingText_NamesOnlyRegisteredTools()
    {
        HashSet<string> registered = [.. Tools.Select(t => t.Name)];
        (string Where, string Text)[] texts =
        [
            ("server instructions", _options.ServerInstructions!),
            .. Tools.Select(t => (t.Name, JsonSerializer.Serialize(t, _readableJson))),
            .. ToolUsageCatalog.Usage.Select(u => ($"usage of {u.Key}", string.Join("\n", u.Value.Values))),
        ];
        (string Where, string Tool)[] named = [.. texts.SelectMany(t => ToolName().Matches(t.Text).Select(m => (t.Where, m.Value))).Distinct()];

        string[] dead = [.. named.Where(n => !registered.Contains(n.Tool) && !_planned.Any(p => p.Tool == n.Tool)).Select(n => $"{n.Where} names {n.Tool}")];
        string[] stale = [.. _planned.Where(p => registered.Contains(p.Tool) || !named.Any(n => n.Tool == p.Tool)).Select(p => p.Tool)];

        Assert.True(dead.Length == 0, "Not a registered tool:\n" + string.Join("\n", dead));
        Assert.True(stale.Length == 0, "Registered now or no longer named; remove from the planned list: " + string.Join(", ", stale));
    }

    [Theory]
    [InlineData("""{"tool": "eplan_project_list"}""", "")]
    [InlineData("""{"tool": "eplan_project_list", "topic": null, "limit": 200}""", "")]
    [InlineData("""{"topic": "errors"}""", "required argument 'tool' is missing")]
    [InlineData("""{"tool": "eplan_project_list", "tpoic": "errors"}""", "unknown argument 'tpoic'")]
    [InlineData("""{"tool": "eplan_project_list", "topic": "error"}""", "'topic' is \"error\", not one of")]
    [InlineData("""{"tool": 3}""", "'tool' is 3, not of type \"string\"")]
    [InlineData("""{"tool": "eplan_project_list", "limit": 1.5}""", "'limit' is 1.5, not of type \"integer\"")]
    [InlineData("""{"tool": "eplan_project_list", "limit": 0}""", "'limit' is 0, below the minimum 1")]
    [InlineData("""{"tool": "eplan_project_list", "limit": 201}""", "'limit' is 201, above the maximum 200")]
    [InlineData("""["eplan_project_list"]""", "the argument object is [\"eplan_project_list\"], not of type \"object\"")]
    public void Violations_FindTheMistakesTheExamplesCanHave(string example, string expected)
    {
        using JsonDocument schema = JsonDocument.Parse("""
            {"type": "object", "additionalProperties": false, "required": ["tool"], "properties": {
              "tool": {"type": "string", "enum": ["eplan_project_list", "eplan_tool_usage"]},
              "topic": {"type": ["string", "null"], "enum": ["examples", "errors", null]},
              "limit": {"type": "integer", "minimum": 1, "maximum": 200}}}
            """);
        using JsonDocument arguments = JsonDocument.Parse(example);

        string[] violations = [.. Violations(schema.RootElement, arguments.RootElement, null)];

        if (expected.Length == 0)
        {
            Assert.Empty(violations);
        }
        else
        {
            Assert.StartsWith(expected, Assert.Single(violations), StringComparison.Ordinal);
        }
    }

    /// <summary>The JSON value after "Example: " in a description, or the rest of the description if that is no JSON.</summary>
    private static string[] DescriptionExample(string description)
    {
        const string Marker = "Example: ";
        int start = description.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        string rest = description[(start + Marker.Length)..];
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(rest));
        try
        {
            return [JsonElement.TryParseValue(ref reader, out JsonElement? value) ? value.Value.GetRawText() : rest];
        }
        catch (JsonException)
        {
            return [rest]; // parsed again by the caller, which reports the error
        }
    }

    /// <summary>Indented (four spaces) and fenced code blocks; each one is an example.</summary>
    private static IEnumerable<string> CodeBlocks(string markdown)
    {
        var block = new List<string>();
        bool fenced = false;
        foreach (string line in markdown.Split('\n').Append(string.Empty))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            else if (fenced || line.StartsWith("    ", StringComparison.Ordinal))
            {
                block.Add(line);
                continue;
            }

            if (block.Count > 0)
            {
                yield return string.Join("\n", block).Trim();
                block.Clear();
            }
        }
    }

    /// <summary>
    /// The JSON Schema keywords the SDK generates for tool arguments: type, enum, minimum, maximum, properties, required,
    /// additionalProperties, items. Enough to catch a wrong example; not a general validator.
    /// </summary>
    private static IEnumerable<string> Violations(JsonElement schema, JsonElement value, string? path)
    {
        string what = path is null ? "the argument object" : $"'{path}'";
        if (schema.TryGetProperty("type", out JsonElement type) && !TypeNames(type).Any(t => IsOfType(value, t)))
        {
            yield return $"{what} is {value.GetRawText()}, not of type {type.GetRawText()}";
            yield break;
        }

        if (schema.TryGetProperty("enum", out JsonElement allowed) && !allowed.EnumerateArray().Any(a => JsonElement.DeepEquals(a, value)))
        {
            yield return $"{what} is {value.GetRawText()}, not one of {allowed.GetRawText()}";
        }

        if (value.ValueKind == JsonValueKind.Number && schema.TryGetProperty("minimum", out JsonElement minimum) && value.GetDouble() < minimum.GetDouble())
        {
            yield return $"{what} is {value.GetRawText()}, below the minimum {minimum.GetRawText()}";
        }

        if (value.ValueKind == JsonValueKind.Number && schema.TryGetProperty("maximum", out JsonElement maximum) && value.GetDouble() > maximum.GetDouble())
        {
            yield return $"{what} is {value.GetRawText()}, above the maximum {maximum.GetRawText()}";
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out JsonElement required))
            {
                foreach (string name in required.EnumerateArray().Select(r => r.GetString()!).Where(r => !value.TryGetProperty(r, out _)))
                {
                    yield return $"required argument '{Join(path, name)}' is missing";
                }
            }

            bool closed = schema.TryGetProperty("additionalProperties", out JsonElement additional) && additional.ValueKind == JsonValueKind.False;
            foreach (JsonProperty argument in value.EnumerateObject())
            {
                if (schema.TryGetProperty("properties", out JsonElement properties) && properties.TryGetProperty(argument.Name, out JsonElement argumentSchema))
                {
                    foreach (string violation in Violations(argumentSchema, argument.Value, Join(path, argument.Name)))
                    {
                        yield return violation;
                    }
                }
                else if (closed)
                {
                    yield return $"unknown argument '{Join(path, argument.Name)}'";
                }
                else if (additional.ValueKind == JsonValueKind.Object)
                {
                    foreach (string violation in Violations(additional, argument.Value, Join(path, argument.Name)))
                    { yield return violation; }
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out JsonElement items))
        {
            foreach ((JsonElement item, int index) in value.EnumerateArray().Select((item, index) => (item, index)))
            {
                foreach (string violation in Violations(items, item, $"{path}[{index}]"))
                {
                    yield return violation;
                }
            }
        }
    }

    private static string Join(string? path, string name) => path is null ? name : $"{path}.{name}";

    private static IEnumerable<string> TypeNames(JsonElement type) =>
        type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()!) : [type.GetString()!];

    private static bool IsOfType(JsonElement value, string type) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => throw new NotSupportedException($"JSON Schema type '{type}'"),
    };

    /// <summary>A tool name as mcp-tool-design 3 forms it: eplan_ plus at least two lower-case words.</summary>
    [GeneratedRegex(@"\beplan_[a-z]+(?:_[a-z]+)+\b")]
    private static partial Regex ToolName();
}
