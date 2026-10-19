using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Checks the arguments against the tool's input schema before the tool runs, because the SDK advertises the schema
/// but does not enforce it: a misspelled argument (<c>limt</c>) would be silently ignored, and a missing required one
/// makes the SDK answer "An error occurred invoking …" without a reason (api-notes C). Unknown and missing arguments
/// are reported together as INVALID_ARGUMENT, so the model can fix them in one go.
/// </summary>
internal static class UnknownArgumentFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Apply(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (context, cancellationToken) =>
        {
            McpServerTool? tool = context.MatchedPrimitive as McpServerTool
                ?? context.Server.ServerOptions.ToolCollection?.FirstOrDefault(t => t.ProtocolTool.Name == context.Params?.Name);
            if (tool is not null && Violations(tool.ProtocolTool.InputSchema, context.Params?.Arguments) is { Count: > 0 } violations)
            {
                return ValueTask.FromResult(ToolResults.InvalidArguments(violations));
            }

            return next(context, cancellationToken);
        };

    /// <summary>Every argument not in the schema's properties, then every required one that is absent or null.</summary>
    internal static List<string> Violations(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        HashSet<string> allowed = Names(schema, "properties");
        IEnumerable<string> unknown = (arguments?.Keys ?? [])
            .Where(name => !allowed.Contains(name))
            .Order(StringComparer.Ordinal)
            .Select(name => $"Unknown argument '{name}'; valid arguments: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed.Order(StringComparer.Ordinal)))}.");
        IEnumerable<string> missing = Names(schema, "required")
            .Where(name => arguments is null || !arguments.TryGetValue(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            .Order(StringComparer.Ordinal)
            .Select(name => $"Missing required argument '{name}'.");
        return [.. unknown, .. missing];
    }

    /// <summary>The property names of an object member, or the strings of an array member, of the schema.</summary>
    private static HashSet<string> Names(JsonElement schema, string member) =>
        !schema.TryGetProperty(member, out JsonElement value) ? []
        : value.ValueKind == JsonValueKind.Object ? [.. value.EnumerateObject().Select(p => p.Name)]
        : value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)]
        : [];
}
