using System.Text.Json;
using EplanMcp.Contracts;
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
            if (tool?.ProtocolTool.Name is PropertyGetTools.Tool or PropertySetTools.Tool)
            {
                // The raw-argument handler runs P5a validation before SDK coercion, with JSON Pointer violations.
                return next(context, cancellationToken);
            }
            if (tool?.ProtocolTool.Name is PlanApplyTools.ApplyTool or PlanApplyTools.TaskTool)
            {
                CallToolResult? invalid = PlanToolResults.Validate(tool.ProtocolTool.InputSchema, context.Params?.Arguments,
                    tool.ProtocolTool.Name == PlanApplyTools.ApplyTool);
                return invalid is null ? next(context, cancellationToken) : ValueTask.FromResult(invalid);
            }
            // Device placement checks raw display objects itself, preserving duplicate members for pointer errors.
            if (tool is not null && Violations(tool.ProtocolTool.InputSchema, context.Params?.Arguments, nestedMembers: tool.ProtocolTool.Name != DeviceWriteTools.PlaceTool) is { Count: > 0 } violations)
            {
                if (tool.ProtocolTool.Name is PageTextReplaceTools.Tool or DeviceWriteTools.PlaceTool)
                {
                    BridgeError error = ToolResults.InvalidArgumentError(violations);
                    error.Execution = ExecutionState.NotStarted;
                    return ValueTask.FromResult(ToolResults.Error(error));
                }

                return ValueTask.FromResult(ToolResults.InvalidArguments(violations));
            }

            return next(context, cancellationToken);
        };

    /// <summary>
    /// Every argument not in the schema's properties, then every required one that is absent or null; then the same for
    /// each element of an array argument whose items are objects, named like <c>pages[1].page_type</c>, and an element
    /// that is not an object at all (the tool would get null for it).
    /// </summary>
    internal static List<string> Violations(JsonElement schema, IDictionary<string, JsonElement>? arguments, string prefix = "", bool nestedMembers = true)
    {
        HashSet<string> allowed = Names(schema, "properties");
        IEnumerable<string> unknown = (arguments?.Keys ?? [])
            .Where(name => !allowed.Contains(name))
            .Order(StringComparer.Ordinal)
            .Select(name => $"Unknown argument '{prefix}{name}'; valid arguments: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed.Order(StringComparer.Ordinal)))}.");
        IEnumerable<string> missing = Names(schema, "required")
            .Where(name => arguments is null || !arguments.TryGetValue(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            .Order(StringComparer.Ordinal)
            .Select(name => $"Missing required argument '{prefix}{name}'.");
        IEnumerable<string> nested = (arguments ?? new Dictionary<string, JsonElement>())
            .Where(a => nestedMembers && a.Value.ValueKind == JsonValueKind.Array && allowed.Contains(a.Key))
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .SelectMany(a => schema.GetProperty("properties").GetProperty(a.Key) is { ValueKind: JsonValueKind.Object } property
                && property.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Object && Names(items, "properties").Count > 0
                ? a.Value.EnumerateArray().SelectMany((item, i) => item.ValueKind == JsonValueKind.Object
                    ? Violations(items, item.EnumerateObject().ToDictionary(p => p.Name, p => p.Value), $"{prefix}{a.Key}[{i}].")
                    : [$"Argument '{prefix}{a.Key}[{i}]' must be an object."])
                : []);
        return [.. unknown, .. missing, .. nested];
    }

    /// <summary>The property names of an object member, or the strings of an array member, of the schema.</summary>
    private static HashSet<string> Names(JsonElement schema, string member) =>
        !schema.TryGetProperty(member, out JsonElement value) ? []
        : value.ValueKind == JsonValueKind.Object ? [.. value.EnumerateObject().Select(p => p.Name)]
        : value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)]
        : [];
}
