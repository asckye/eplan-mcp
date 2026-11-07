using System.Text.Json;
using EplanMcp.Contracts;
using Microsoft.Extensions.DependencyInjection;
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
            if (tool?.ProtocolTool.Name == MacroWriteTools.InsertTool && context.Params?.Arguments?.ContainsKey("items") == true)
            {
                List<string> macroViolations = MacroWriteTools.BatchViolations(tool.ProtocolTool.InputSchema, context.Params.Arguments);
                if (macroViolations.Count == 0)
                {
                    return next(context, cancellationToken);
                }
                BridgeError error = ToolResults.InvalidArgumentError(macroViolations);
                error.Execution = ExecutionState.NotStarted;
                return ValueTask.FromResult(ToolResults.Error(error));
            }
            if (tool?.ProtocolTool.Name is SelectionTools.GetTool or SelectionTools.SetTool or ActionTryTools.Tool
                && (context.Params?.Arguments ?? new Dictionary<string, JsonElement>()).Any(p => p.Key is "locators" or "selection" && (p.Value.ValueKind != JsonValueKind.Array || p.Value.EnumerateArray().Any(l => l.ValueKind != JsonValueKind.Object
                    || l.EnumerateObject().Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != l.EnumerateObject().Count()
                    || l.EnumerateObject().Any(f => f.Name is "type" or "oid" ? f.Value.ValueKind != JsonValueKind.Number || !f.Value.TryGetInt64(out long number) || number <= 0 || f.Name == "type" && number > int.MaxValue : f.Value.ValueKind != JsonValueKind.String)))))
            {
                BridgeError error = ToolResults.InvalidArgumentError(["Use locator objects with string fields and integer type/oid; nulls, coercion and duplicate fields are invalid."]);
                error.Execution = tool.ProtocolTool.Name == ActionTryTools.Tool ? ExecutionState.NotStarted : null;
                return ValueTask.FromResult(ToolResults.Error(error));
            }
            if (tool?.ProtocolTool.Name == ActionTryTools.Tool && context.Params?.Arguments?.TryGetValue("parameters", out JsonElement parameters) == true && parameters.ValueKind != JsonValueKind.Object)
            {
                BridgeError error = ToolResults.InvalidArgumentError(["parameters must be a flat object."]);
                error.Execution = ExecutionState.NotStarted;
                return InvalidAsync(context, error, cancellationToken);
            }
            if (tool?.ProtocolTool.Name is PropertyGetTools.Tool or PropertySetTools.Tool or PlacementCreateTools.Tool or AnnotationCreateTools.Tool)
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
                if (tool.ProtocolTool.Name is PageTextReplaceTools.Tool or DeviceWriteTools.PlaceTool or ConnectionCreateTools.Tool or PlacementUpdateTools.Tool or PlacementEditTools.Tool or GraphicEditTools.Tool or PlacementDeleteTools.Tool or ApiTryTools.Tool or ActionTryTools.Tool)
                {
                    BridgeError error = ToolResults.InvalidArgumentError(violations);
                    error.Execution = ExecutionState.NotStarted;
                    return InvalidAsync(context, error, cancellationToken);
                }

                return ValueTask.FromResult(ToolResults.InvalidArguments(violations));
            }

            return next(context, cancellationToken);
        };

    private static async ValueTask<CallToolResult> InvalidAsync(RequestContext<CallToolRequestParams> context, BridgeError error, CancellationToken cancellationToken)
    {
        var args = context.Params?.Arguments;
        string? Text(string name) => args?.TryGetValue(name, out JsonElement value) == true && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        JsonElement given = args?.TryGetValue("parameters", out JsonElement supplied) == true ? supplied : default;
        if (context.Params?.Name == ApiTryTools.Tool && Text("group") is { } group)
        { return ToolResults.Error(error, ApiCallTemplates.Closest(context.Server.Services!.GetRequiredService<ApiIndex>(), group, given)); }
        if (context.Params?.Name == ActionTryTools.Tool && Text("action") is { } action)
        { return ToolResults.Error(error, ActionCallTemplates.Closest(await context.Server.Services!.GetRequiredService<ActionCatalogReader>().ReadAsync(cancellationToken).ConfigureAwait(false), action, Text("type"), given)); }
        return ToolResults.Error(error);
    }

    /// <summary>
    /// Every argument not in the schema's properties, then every required one that is absent or null; then the same for
    /// each element of an array argument whose items are objects, named like <c>pages[1].page_type</c>, and an element
    /// that is not an object at all (the tool would get null for it).
    /// </summary>
    internal static List<string> Violations(JsonElement schema, IDictionary<string, JsonElement>? arguments, string prefix = "", bool nestedMembers = true, bool jsonPointers = false)
    {
        HashSet<string> allowed = Names(schema, "properties");
        IEnumerable<string> unknown = (arguments?.Keys ?? [])
            .Where(name => !allowed.Contains(name))
            .Order(StringComparer.Ordinal)
            .Select(name => jsonPointers
                ? $"{prefix}/{name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}: Unknown argument."
                : $"Unknown argument '{prefix}{name}'; valid arguments: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed.Order(StringComparer.Ordinal)))}.");
        IEnumerable<string> missing = Names(schema, "required")
            .Where(name => arguments is null || !arguments.TryGetValue(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            .Order(StringComparer.Ordinal)
            .Select(name => jsonPointers ? $"{prefix}/{name}: Missing required argument." : $"Missing required argument '{prefix}{name}'.");
        IEnumerable<string> nested = (arguments ?? new Dictionary<string, JsonElement>())
            .Where(a => nestedMembers && a.Value.ValueKind == JsonValueKind.Array && allowed.Contains(a.Key))
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .SelectMany(a => schema.GetProperty("properties").GetProperty(a.Key) is { ValueKind: JsonValueKind.Object } property
                && property.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Object && Names(items, "properties").Count > 0
                ? a.Value.EnumerateArray().SelectMany((item, i) => item.ValueKind == JsonValueKind.Object
                    ? Violations(items, item.EnumerateObject().ToDictionary(p => p.Name, p => p.Value), jsonPointers ? $"{prefix}/{a.Key}/{i}" : $"{prefix}{a.Key}[{i}].", jsonPointers: jsonPointers)
                    : [jsonPointers ? $"{prefix}/{a.Key}/{i}: Supply an insertion object." : $"Argument '{prefix}{a.Key}[{i}]' must be an object."])
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
