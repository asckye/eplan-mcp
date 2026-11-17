using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The discovery profile's own tools (ADR-0057, docs/tools/eplan_tool_search.md, eplan_tool_read.md, eplan_tool_write.md):
/// find a tool, then call it through the dispatcher of its safety level. A dispatched call runs the target exactly as a
/// direct call would: the same argument filter, the same request context, the target's own approval, scratch rule and
/// audit. Parameter names are snake_case because the SDK publishes them as written.
/// </summary>
[McpServerToolType]
internal sealed class DiscoveryTools
{
    public const string SearchTool = "eplan_tool_search";
    public const string ReadTool = "eplan_tool_read";
    public const string WriteTool = "eplan_tool_write";
    public const int MaxQueryLength = 200;
    public const int MaxLimit = 20;

    [McpServerTool(Name = SearchTool, Title = "Find the EplanMcp tool for a task", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolSearchResult))]
    [Description(
        "Finds EplanMcp tools for a task by words in their names, titles, descriptions and usage, and says which dispatcher " +
        "calls each: eplan_tool_read for read-only tools, eplan_tool_write for the rest. Then read the tool's usage with " +
        "eplan_tool_usage and call it through that dispatcher. Works without EPLAN. Example: {\"query\": \"page create\"}.")]
    public static CallToolResult Search(
        ToolRegistry registry,
        [Description("Words of the task, 1-200 characters, e.g. 'pdf export'.")] string query,
        [Description("Max tools, 1-20.")] int limit = 8)
    {
        var violations = new List<string>();
        string text = (query ?? string.Empty).Trim();
        if (text.Length is 0 or > MaxQueryLength)
        {
            violations.Add(string.Create(CultureInfo.InvariantCulture, $"'query' must have 1 to {MaxQueryLength} characters: words of the task."));
        }

        if (limit is < 1 or > MaxLimit)
        {
            violations.Add(string.Create(CultureInfo.InvariantCulture, $"'limit' must be between 1 and {MaxLimit}."));
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        string[] terms = [.. text.ToLowerInvariant().Split([' ', '_', '-', ','], StringSplitOptions.RemoveEmptyEntries)];
        var ranked = registry.Forwardable
            .Select(tool => (tool.ProtocolTool, Score: Score(tool.ProtocolTool, terms)))
            .Where(hit => hit.Score > 0)
            .OrderByDescending(hit => hit.Score).ThenBy(hit => hit.ProtocolTool.Name, StringComparer.Ordinal)
            .ToList();
        var result = new ToolSearchResult
        {
            Tools = [.. ranked.Take(limit).Select(hit => new ToolSearchResult.Entry
            {
                Name = hit.ProtocolTool.Name,
                Title = hit.ProtocolTool.Title ?? hit.ProtocolTool.Annotations?.Title ?? string.Empty,
                Safety = Safety(hit.ProtocolTool),
                Dispatcher = hit.ProtocolTool.Annotations?.ReadOnlyHint == true ? ReadTool : WriteTool,
                Summary = FirstSentence(hit.ProtocolTool.Description ?? string.Empty),
            })],
            Total = ranked.Count,
        };
        string summary = result.Total == 0
            ? $"No tool matches '{text}'. Try other words, or call eplan_tool_usage with tool: eplan_tool_search for examples."
            : string.Create(CultureInfo.InvariantCulture, $"Found {result.Tools.Count} of {result.Total} tools.");
        return ToolResults.Success(summary, result);
    }

    [McpServerTool(Name = ReadTool, Title = "Call a read-only EplanMcp tool", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description(
        "Calls one read-only EplanMcp tool by name with its own arguments and returns its result unchanged. Write tools go " +
        "through eplan_tool_write. Find tools with eplan_tool_search and their arguments with eplan_tool_usage. " +
        "Example: {\"tool\": \"eplan_page_list\", \"arguments\": {\"project\": \"Line1\"}}.")]
    public static ValueTask<CallToolResult> Read(
        RequestContext<CallToolRequestParams> context,
        ToolRegistry registry,
        [Description("A read-only tool from eplan_tool_search.")] string tool,
        [Description("The tool's own arguments, as for a direct call.")] JsonElement? arguments = null,
        CancellationToken cancellationToken = default) =>
        DispatchAsync(context, registry, tool, arguments, write: false, cancellationToken);

    [McpServerTool(Name = WriteTool, Title = "Call a writing EplanMcp tool", ReadOnly = false, Destructive = true,
        Idempotent = false, OpenWorld = false)]
    [Description(
        "Calls one EplanMcp write or destructive tool by name with its own arguments and returns its result unchanged. The " +
        "tool keeps its own rules: omitted dry_run previews, writes outside scratch and master data need the engineer's " +
        "approval, and every write is audited. Read-only tools go through eplan_tool_read. " +
        "Example: {\"tool\": \"eplan_page_create\", \"arguments\": {\"project\": \"Line1\", \"pages\": [{\"name\": \"=GAA+A1&EFS1/9\"}]}}.")]
    public static ValueTask<CallToolResult> Write(
        RequestContext<CallToolRequestParams> context,
        ToolRegistry registry,
        [Description("A write tool from eplan_tool_search.")] string tool,
        [Description("The tool's own arguments, as for a direct call.")] JsonElement? arguments = null,
        CancellationToken cancellationToken = default) =>
        DispatchAsync(context, registry, tool, arguments, write: true, cancellationToken);

    /// <summary>Lists the tools each dispatcher reaches as the enum of its <c>tool</c> argument.</summary>
    public static void Configure(McpServerOptions options, ToolRegistry registry)
    {
        foreach (McpServerTool dispatcher in (options.ToolCollection ?? []).Where(t => t.ProtocolTool.Name is ReadTool or WriteTool))
        {
            bool write = dispatcher.ProtocolTool.Name == WriteTool;
            JsonNode schema = JsonNode.Parse(dispatcher.ProtocolTool.InputSchema.GetRawText())!;
            schema["properties"]!["tool"]!["enum"] = JsonSerializer.SerializeToNode(Reachable(registry, write).Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal));
            dispatcher.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
        }
    }

    internal static string Safety(Tool tool) => tool.Annotations?.ReadOnlyHint == true ? "read"
        : tool.Annotations?.DestructiveHint == true ? "destructive" : "write";

    private static IEnumerable<McpServerTool> Reachable(ToolRegistry registry, bool write) =>
        registry.Forwardable.Where(t => (t.ProtocolTool.Annotations?.ReadOnlyHint == true) != write);

    private static async ValueTask<CallToolResult> DispatchAsync(RequestContext<CallToolRequestParams> context, ToolRegistry registry,
        string tool, JsonElement? arguments, bool write, CancellationToken cancellationToken)
    {
        var violations = new List<string>();
        McpServerTool? target = registry.Forwardable.FirstOrDefault(t => t.ProtocolTool.Name == tool);
        // The trusted side decides the safety level, from the target's own annotations (UI-16), never from the caller.
        if (target is null)
        {
            violations.Add($"'tool' names no tool {(write ? WriteTool : ReadTool)} can call; find one with {SearchTool}.");
        }
        else if ((target.ProtocolTool.Annotations?.ReadOnlyHint == true) == write)
        {
            violations.Add(write
                ? $"'{tool}' only reads; call {ReadTool} for this tool."
                : $"'{tool}' writes; call {WriteTool} for this tool.");
        }

        if (arguments is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Null and not JsonValueKind.Undefined })
        {
            violations.Add("'arguments' must be an object of the tool's own arguments.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        Dictionary<string, JsonElement> forwarded = arguments is { ValueKind: JsonValueKind.Object } given
            ? given.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal)
            : [];
        // The same request: progress goes to the caller's token through Meta, services and user are the caller's.
        var inner = new RequestContext<CallToolRequestParams>(context.Server, context.JsonRpcRequest,
            new CallToolRequestParams { Name = tool, Arguments = forwarded, Meta = context.Params?.Meta })
        {
            MatchedPrimitive = target,
            Services = context.Services,
            User = context.User,
        };
        McpRequestHandler<CallToolRequestParams, CallToolResult> run = UnknownArgumentFilter.Apply((c, t) => target!.InvokeAsync(c, t));
        return await run(inner, cancellationToken).ConfigureAwait(false);
    }

    private static int Score(Tool tool, string[] terms)
    {
        string name = tool.Name.ToLowerInvariant();
        string[] parts = name.Split('_');
        string title = (tool.Title ?? tool.Annotations?.Title ?? string.Empty).ToLowerInvariant();
        string description = (tool.Description ?? string.Empty).ToLowerInvariant();
        string usage = ToolUsageCatalog.Usage.TryGetValue(tool.Name, out IReadOnlyDictionary<string, string>? sections)
            ? string.Join("\n", sections.Values).ToLowerInvariant() : string.Empty;
        int score = 0;
        foreach (string term in terms)
        {
            score += parts.Contains(term, StringComparer.Ordinal) ? 6 : name.Contains(term, StringComparison.Ordinal) ? 4 : 0;
            score += title.Contains(term, StringComparison.Ordinal) ? 3 : 0;
            score += description.Contains(term, StringComparison.Ordinal) ? 2 : 0;
            score += usage.Contains(term, StringComparison.Ordinal) ? 1 : 0;
        }

        return score;
    }

    private static string FirstSentence(string text)
    {
        int end = text.IndexOf(". ", StringComparison.Ordinal);
        string first = end < 0 ? text : text[..(end + 1)];
        return first.Length <= 200 ? first : first[..197] + "...";
    }
}

/// <summary>Output of <c>eplan_tool_search</c>.</summary>
internal sealed class ToolSearchResult
{
    [Description("Matching tools, best first.")]
    public List<Entry> Tools { get; set; } = [];

    [Description("How many tools match before the limit.")]
    public int Total { get; set; }

    internal sealed class Entry
    {
        [Description("The tool's name, for eplan_tool_usage and the dispatcher.")]
        public string Name { get; set; } = string.Empty;

        [Description("The tool's title.")]
        public string Title { get; set; } = string.Empty;

        [Description("read, write or destructive, from the tool's own annotations.")]
        public string Safety { get; set; } = string.Empty;

        [Description("eplan_tool_read or eplan_tool_write: the dispatcher that calls this tool.")]
        public string Dispatcher { get; set; } = string.Empty;

        [Description("The first sentence of the tool's description.")]
        public string Summary { get; set; } = string.Empty;
    }
}
