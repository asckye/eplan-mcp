using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Agent;

/// <summary>The command line of one attempt (agent evals design 4.2, step 3; each option is checked in api-notes C).</summary>
internal static class ClaudeCommand
{
    /// <summary>Q1 of the design fixes the model; the report records it.</summary>
    public const string DefaultModel = "claude-sonnet-5-5";

    // A run within budget takes a turn per tool call at most, one for StructuredOutput and one for the closing text;
    // the rest leaves room for an answer the schema rejects once or twice.
    private const int MaxTurnsHeadroom = 5;

    /// <summary>
    /// The arguments without the prompt, which goes to stdin: a prompt starting with "-" would be read as an option.
    /// <paramref name="mcpConfigFile"/> is the path of a file holding <see cref="McpConfig"/>.
    /// </summary>
    public static IReadOnlyList<string> Arguments(EvalTask task, string model, string mcpConfigFile) =>
    [
        "-p",
        "--output-format", "stream-json", "--verbose", "--no-session-persistence",

        // Isolation: no user or project settings, project memory, built-in tools, skills or other MCP servers.
        "--restricted", "--tools", string.Empty, "--disable-slash-commands",
        "--strict-mcp-config", "--mcp-config", mcpConfigFile,

        // StructuredOutput delivers the answer; allowing it costs nothing even if dontAsk lets it through anyway.
        "--allowedTools", $"mcp__{AgentTrace.ServerName},{AgentTrace.AnswerTool}",
        "--permission-mode", "dontAsk", "--permission-prompts", "none",

        "--json-schema", task.Answer.ToJsonString(),
        "--model", model,

        // The runner counts the tool calls itself; these two only catch what it might miss.
        "--max-budget-usd", task.Budget.MaxUsd.ToString(CultureInfo.InvariantCulture),
        "--max-turns", (task.Budget.MaxToolCalls + MaxTurnsHeadroom).ToString(CultureInfo.InvariantCulture),
    ];

    /// <summary>An MCP configuration with eplanmcp as the only server, started over stdio.</summary>
    public static string McpConfig(string eplanmcp) => new JsonObject
    {
        ["mcpServers"] = new JsonObject
        {
            [AgentTrace.ServerName] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = eplanmcp,
                ["args"] = new JsonArray("serve"),
            },
        },
    }.ToJsonString();
}
