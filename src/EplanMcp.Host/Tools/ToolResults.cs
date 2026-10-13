using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The two result shapes every tool returns (mcp-tool-design 6 and 7): success = one-line summary + the same data
/// as JSON text + <c>structuredContent</c>; error = <c>isError</c> with <c>{"error": …}</c> as text and no
/// structured content, so a client validating <c>outputSchema</c> is never handed an error object.
/// </summary>
internal static class ToolResults
{
    public static CallToolResult Success<T>(string summary, T data)
    {
        JsonElement structured = JsonSerializer.SerializeToElement(data, HostJson.Options);
        return new CallToolResult
        {
            StructuredContent = structured,
            Content =
            [
                new TextContentBlock { Text = summary },
                new TextContentBlock { Text = structured.GetRawText() },
            ],
        };
    }

    public static CallToolResult Error(BridgeError error)
    {
        var body = new JsonObject { ["error"] = JsonSerializer.SerializeToNode(error, HostJson.Options) };
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = body.ToJsonString(HostJson.Options) }],
        };
    }

    /// <summary>Argument problems, all at once (mcp-tool-design 6.2), as INVALID_ARGUMENT.</summary>
    public static CallToolResult InvalidArguments(IReadOnlyList<string> violations) => Error(new BridgeError
    {
        Reason = ErrorReason.InvalidArgument,
        Message = "Invalid arguments: " + string.Join(" ", violations),
        Metadata = new Dictionary<string, string> { ["violations"] = string.Join("\n", violations) },
    });
}
