using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

internal sealed record PageDisplay(bool? PageShown = null, string? Hint = null)
{
    public const string Description = "Open or focus this page in the EPLAN editor before drawing so the user can watch; UI only, no data change.";

    public static Task<PageDisplay> ShowAsync(IBridgeClient bridge, bool showPage, JsonElement input, CancellationToken cancellationToken) =>
        ShowAsync(bridge, showPage,
            input.TryGetProperty("project", out JsonElement project) && project.ValueKind == JsonValueKind.String ? project.GetString()! : string.Empty,
            input.TryGetProperty("page", out JsonElement page) && page.ValueKind == JsonValueKind.String ? page.GetString()! : string.Empty, cancellationToken);

    public static async Task<PageDisplay> ShowAsync(IBridgeClient bridge, bool showPage, string project, string page, CancellationToken cancellationToken)
    {
        if (!showPage)
        { return new(); }
        try
        {
            PageOpenData data = await bridge.InvokeAsync<PageOpenArgs, PageOpenData>(OperationNames.PageOpen,
                new() { Project = project?.Trim() ?? string.Empty, Page = page ?? string.Empty }, cancellationToken).ConfigureAwait(false);
            return data.EditorOpened ? new(true) : new(false, "Page was not shown: editor was not opened.");
        }
        catch (BridgeCallException ex)
        { return new(false, "Page was not shown: " + ResultShaper.CleanText(ex.Error.Message)); }
    }

    public CallToolResult Apply(CallToolResult result)
    {
        if (PageShown is null)
        { return result; }
        JsonObject body = JsonNode.Parse(result.StructuredContent?.GetRawText() ?? ((TextContentBlock)result.Content[0]).Text)!.AsObject();
        body["pageShown"] = PageShown;
        if (Hint is not null)
        { body["hint"] = string.Join(" ", new[] { body["hint"]?.GetValue<string>(), Hint }.OfType<string>()); }
        if (result.StructuredContent is null)
        { result.Content = [new TextContentBlock { Text = body.ToJsonString(HostJson.Options) }]; return result; }
        return ToolResults.Success(((TextContentBlock)result.Content[0]).Text, body);
    }
}
