using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class PageOpenTools
{
    public const string Tool = "eplan_page_open";

    [McpServerTool(Name = Tool, Title = "Open a page in the EPLAN editor", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PageOpenResult))]
    [Description("Opens or focuses a page editor, changing the visible page and UI focus. Requires an already open project and an exact page name; never uses UI selection. No project data changes, saves or undo steps. Repeated calls refocus the same page. Example: {\"project\":\"Line1\",\"page\":\"=GAA+A1&EFS1/1\"}.")]
    public static async Task<CallToolResult> OpenAsync(
        IBridgeClient bridge,
        [Description("Open project name or path from eplan_project_list.")] string project,
        [Description("Exact full page name from eplan_page_list; case-sensitive.")] string page,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        { violations.Add("/project is required; choose an open project from eplan_project_list."); }
        if (string.IsNullOrWhiteSpace(page))
        { violations.Add("/page is required; use the exact full name from eplan_page_list."); }
        if (violations.Count > 0)
        { return ToolResults.InvalidArguments(violations); }
        try
        {
            PageOpenData data = await bridge.InvokeAsync<PageOpenArgs, PageOpenData>(OperationNames.PageOpen,
                new() { Project = project.Trim(), Page = page }, cancellationToken).ConfigureAwait(false);
            return ToolResults.Success("Page editor opened or focused.", new PageOpenResult
            {
                Project = ResultShaper.CleanText(data.Project),
                Page = ResultShaper.CleanText(data.Page),
                EditorOpened = data.EditorOpened,
            });
        }
        catch (BridgeCallException ex) { return ToolResults.Error(ex.Error); }
    }
}
