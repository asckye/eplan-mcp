using System.Text.Json.Nodes;

namespace EplanMcp.Evals.Agent;

/// <summary>
/// The command line of one attempt with <c>codex exec</c> instead of <c>claude -p</c> (owner 2026-11-10). Isolation
/// works differently: no user config or rules, a read-only sandbox for shell commands, eplanmcp as the only MCP server.
/// Codex reports tokens, not US dollars, so only the tool-call and time limits of the budget apply.
/// </summary>
internal static class CodexCommand
{
    /// <summary>The answer schema file, written next to the transcript.</summary>
    public const string SchemaFile = "answer-schema.json";

    /// <summary>The arguments without the prompt, which goes to stdin (<c>-</c>).</summary>
    public static IReadOnlyList<string> Arguments(ClaudeSetup setup, string workDirectory, string schemaFile)
    {
        List<string> arguments =
        [
            "exec", "--json", "--ephemeral", "--skip-git-repo-check", "--ignore-user-config", "--ignore-rules",
            "-s", "read-only", "-C", workDirectory, "--output-schema", schemaFile,
            "-c", $"mcp_servers.{AgentTrace.ServerName}.command={Toml(setup.Eplanmcp ?? string.Empty)}",
            "-c", $"mcp_servers.{AgentTrace.ServerName}.args=[\"serve\"]",
            "-c", $"mcp_servers.{AgentTrace.ServerName}.env.{ClaudeCommand.HomeVariable}={Toml(setup.Home ?? string.Empty)}",
        ];
        if (setup.Model != ClaudeCommand.DefaultModel)
        {
            arguments.AddRange(["-m", setup.Model]);
        }

        arguments.Add("-");
        return arguments;
    }

    /// <summary>
    /// The task's answer schema in the strict form structured outputs need: every object closed and all its properties
    /// required. The answers are a few fields, so requiring all of them changes nothing the checks look at.
    /// </summary>
    public static JsonNode StrictSchema(JsonNode schema)
    {
        JsonNode copy = schema.DeepClone();
        Close(copy);
        return copy;
    }

    private static void Close(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                Close(item);
            }

            return;
        }

        if (node is not JsonObject schema)
        {
            return;
        }

        if (schema["properties"] is JsonObject properties)
        {
            schema["additionalProperties"] = false;
            schema["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]);
        }

        foreach (KeyValuePair<string, JsonNode?> child in schema.ToList())
        {
            Close(child.Value);
        }
    }

    /// <summary>A TOML basic string.</summary>
    private static string Toml(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
