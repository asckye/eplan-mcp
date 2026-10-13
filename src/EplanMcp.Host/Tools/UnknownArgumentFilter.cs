using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Enforces <c>additionalProperties: false</c>. The SDK advertises the schema but does not validate arguments
/// against it, so a misspelled argument (<c>limt</c>) would be silently ignored and the model would believe it
/// had been applied. Every unknown argument is reported at once as INVALID_ARGUMENT, before the tool runs.
/// </summary>
internal static class UnknownArgumentFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Apply(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (context, cancellationToken) =>
        {
            McpServerTool? tool = context.MatchedPrimitive as McpServerTool
                ?? context.Server.ServerOptions.ToolCollection?.FirstOrDefault(t => t.ProtocolTool.Name == context.Params?.Name);
            if (tool is not null && context.Params?.Arguments is { Count: > 0 } arguments)
            {
                HashSet<string> allowed = AllowedNames(tool.ProtocolTool.InputSchema);
                List<string> violations =
                [
                    .. arguments.Keys
                        .Where(name => !allowed.Contains(name))
                        .Order(StringComparer.Ordinal)
                        .Select(name => $"Unknown argument '{name}'; valid arguments: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed.Order(StringComparer.Ordinal)))}."),
                ];
                if (violations.Count > 0)
                {
                    return ValueTask.FromResult(ToolResults.InvalidArguments(violations));
                }
            }

            return next(context, cancellationToken);
        };

    private static HashSet<string> AllowedNames(JsonElement schema) =>
        schema.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object
            ? [.. properties.EnumerateObject().Select(p => p.Name)]
            : [];
}
