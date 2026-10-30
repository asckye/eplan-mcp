using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Project library discovery outside writes (api-notes A52).</summary>
[McpServerToolType]
internal sealed class SymbolTools
{
    public const string Tool = "eplan_symbol_search";

    [McpServerTool(Name = Tool, Title = "Search project symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SymbolSearchResult))]
    [Description("Finds symbols, descriptions and pins (mm). Example: {\"project\":\"Line1\"}.")]
    public static async Task<CallToolResult> SearchAsync(
        IBridgeClient bridge,
        [Description("Project.")] string project,
        [Description("Exact library, any case.")] string? library = null,
        [Description("Name text.")] string? name_contains = null,
        [AllowedValues("function", "routing", "t_node", "potential_point", "potential_definition", "interruption_point", "location_box", "cable_definition", "plc_box", "terminal", "other")] string? kind = null,
        [Range(1, 200)] int limit = 50,
        [Description("nextCursor.")] string? cursor = null,
        [Description("Symbol name or description text, any case, e.g. pushbutton.")] string? text_contains = null,
        [Description("ISO; default: project display, then others.")] string? language = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        { violations.Add("'project' is required; choose an open project from eplan_project_list."); }
        if (limit is < 1 or > 200)
        { violations.Add("'limit' must be between 1 and 200."); }
        if (kind is not null && !SymbolKinds.All.Contains(kind))
        { violations.Add("'kind' must be a value from its enum; omit it for all kinds."); }
        ToolArguments.CheckLanguage(language, violations);
        var args = new SymbolSearchArgs { Project = project?.Trim(), Library = library?.Trim(), NameContains = name_contains?.Trim(), TextContains = text_contains?.Trim(), Language = ToolArguments.Optional(language), Kind = kind, Limit = limit };
        string fingerprint = PageCursor.Fingerprint(Tool, args.Project, args.Library, args.NameContains, kind, args.TextContains, args.Language);
        args.Offset = PageCursor.Offset(cursor, fingerprint, Tool, violations);
        if (violations.Count > 0)
        { return ToolResults.InvalidArguments(violations); }
        ListData<SymbolSearchItem> data;
        try
        {
            data = await bridge.InvokeAsync<SymbolSearchArgs, ListData<SymbolSearchItem>>(OperationNames.SymbolSearch, args, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex) { return ToolResults.Error(ex.Error); }
        List<SymbolSearchResult.Item> items = [.. data.Items.Select(item => new SymbolSearchResult.Item
        {
            Library = ResultShaper.CleanText(item.Library), Name = ResultShaper.CleanText(item.Name),
            Description = ResultShaper.CleanText(item.Description), Type = item.Type, Kind = item.Kind,
            FunctionCategory = item.FunctionCategory, PlcInputDefinition = item.PlcInputDefinition,
            Variant = item.Variant, ConnectionPoints = item.ConnectionPoints,
        })];

        int next = args.Offset + data.Items.Count;
        var result = new SymbolSearchResult { Project = ResultShaper.CleanText(data.Project), Symbols = items, Total = data.Total, NextCursor = next < data.Total ? PageCursor.Encode(next, fingerprint) : null };
        ResultShaper.FitPage(result, args.Offset, fingerprint);
        return ToolResults.Success(string.Create(CultureInfo.InvariantCulture, $"Found {result.Symbols.Count} of {result.Total} symbol variants."), result);
    }
}
