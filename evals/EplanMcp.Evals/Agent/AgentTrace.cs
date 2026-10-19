using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Agent;

/// <summary>A tool call of the main conversation; <see cref="IsError"/> stays null until its result arrives.</summary>
internal sealed class ToolCall(string id, string tool)
{
    public string Id { get; } = id;

    /// <summary>The eplanmcp tool name, without the <c>mcp__eplan__</c> prefix Claude Code adds.</summary>
    public string Tool { get; } = tool;

    public bool? IsError { get; set; }
}

internal sealed record TokenUsage(long Input, long Output, long CacheRead, long CacheCreation);

/// <summary>The <c>result</c> event that ends a run. <see cref="Answer"/> is the structured output, if there is one.</summary>
internal sealed record AgentResult(
    string Subtype,
    bool IsError,
    string? TerminalReason,
    string? Message,
    JsonNode? Answer,
    decimal CostUsd,
    TokenUsage Usage,
    int Turns,
    TimeSpan Duration);

/// <summary>
/// Reads the stream-json output of <c>claude -p</c> line by line (agent evals design 4.2; formats in api-notes C). Only
/// the main conversation counts: messages of subagents carry a <c>parent_tool_use_id</c> and are skipped, and the
/// <c>StructuredOutput</c> call that delivers the answer is not a tool call.
/// </summary>
internal sealed class AgentTrace
{
    public const string ServerName = "eplan";
    public const string AnswerTool = "StructuredOutput";
    private const string ToolPrefix = "mcp__" + ServerName + "__";

    private readonly List<ToolCall> _calls = [];

    public IReadOnlyList<ToolCall> Calls => _calls;

    /// <summary>The tools the session offers the model, from <c>system/init</c>; null until it arrives.</summary>
    public IReadOnlyList<string>? Tools { get; private set; }

    /// <summary>The status of the eplanmcp server in <c>system/init</c>, such as <c>connected</c> or <c>failed</c>.</summary>
    public string? ServerStatus { get; private set; }

    /// <summary>The <c>error</c> of an assistant message Claude Code made up itself, such as <c>authentication_failed</c>.</summary>
    public string? ApiError { get; private set; }

    public AgentResult? Result { get; private set; }

    /// <summary>Lines that are not JSON objects; Claude Code writes none, so any is worth a look in the transcript.</summary>
    public int Unreadable { get; private set; }

    /// <summary>Failed calls after which the same tool succeeded: the model corrected itself.</summary>
    public int Corrected => _calls.Select((c, i) => (c, i)).Count(e =>
        e.c.IsError == true && _calls.Skip(e.i + 1).Any(later => later.Tool == e.c.Tool && later.IsError == false));

    /// <summary>
    /// Why the session is not the isolated one the evals need (design 4.2, step 3), or null when it is or cannot be
    /// told yet. A tool call before <c>system/init</c> is a problem too: the tools on offer were never shown.
    /// </summary>
    public string? IsolationProblem()
    {
        if (Tools is null)
        {
            return _calls.Count > 0 ? "a tool call came before the init event" : null;
        }

        if (Tools.FirstOrDefault(t => t != AnswerTool && !t.StartsWith(ToolPrefix, StringComparison.Ordinal)) is { } other)
        {
            return $"the model could use {other}, not only eplanmcp";
        }

        return ServerStatus != "connected" ? $"eplanmcp did not connect (status {ServerStatus ?? "missing"})"
            : !Tools.Any(t => t.StartsWith(ToolPrefix, StringComparison.Ordinal)) ? "eplanmcp offers no tools"
            : null;
    }

    public void Read(string line)
    {
        JsonObject? message;
        try
        {
            message = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            Unreadable++;
            return;
        }

        bool main = message["parent_tool_use_id"] is null;
        switch (Text(message["type"]))
        {
            case "system" when Text(message["subtype"]) == "init":
                Tools = message["tools"] is JsonArray tools ? [.. tools.Select(Text).OfType<string>()] : [];
                ServerStatus = (message["mcp_servers"] as JsonArray)?.OfType<JsonObject>()
                    .FirstOrDefault(s => Text(s["name"]) == ServerName) is { } server ? Text(server["status"]) : null;
                break;
            case "assistant" when main:
                ApiError ??= Text(message["error"]);
                foreach (JsonObject use in Blocks(message, "tool_use"))
                {
                    if (Text(use["name"]) is { } name && name != AnswerTool)
                    {
                        _calls.Add(new ToolCall(Text(use["id"]) ?? string.Empty, name.StartsWith(ToolPrefix, StringComparison.Ordinal) ? name[ToolPrefix.Length..] : name));
                    }
                }

                break;
            case "user" when main:
                foreach (JsonObject result in Blocks(message, "tool_result"))
                {
                    if (_calls.FirstOrDefault(c => c.Id == Text(result["tool_use_id"])) is { } call)
                    {
                        call.IsError = result["is_error"] is JsonValue flag && flag.GetValueKind() == JsonValueKind.True;
                    }
                }

                break;
            case "result":
                Result = ReadResult(message);
                break;
        }
    }

    private static AgentResult ReadResult(JsonObject result)
    {
        JsonNode? usage = result["usage"];
        string? errors = result["errors"] is JsonArray list ? string.Join("; ", list.Select(Text).OfType<string>()) : null;
        return new AgentResult(
            Text(result["subtype"]) ?? string.Empty,
            result["is_error"] is JsonValue flag && flag.GetValueKind() == JsonValueKind.True,
            Text(result["terminal_reason"]),
            errors is { Length: > 0 } ? errors : Text(result["result"]),
            result["structured_output"]?.DeepClone(),
            Number(result["total_cost_usd"]),
            new TokenUsage(
                (long)Number(usage?["input_tokens"]),
                (long)Number(usage?["output_tokens"]),
                (long)Number(usage?["cache_read_input_tokens"]),
                (long)Number(usage?["cache_creation_input_tokens"])),
            (int)Number(result["num_turns"]),
            TimeSpan.FromMilliseconds((double)Number(result["duration_ms"])));
    }

    private static IEnumerable<JsonObject> Blocks(JsonObject message, string type) =>
        (message["message"]?["content"] as JsonArray)?.OfType<JsonObject>().Where(b => Text(b["type"]) == type) ?? [];

    private static string? Text(JsonNode? node) => Comparison.Text(node);

    private static decimal Number(JsonNode? node) => Comparison.Number(node) ?? 0;
}
