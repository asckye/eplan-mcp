using System.ComponentModel;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class AnnotationCreateTools
{
    internal const string Tool = "eplan_annotation_create";

    [McpServerTool(Name = Tool, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    public static Task<CallToolResult> CreateAsync(IBridgeClient bridge, WritePipeline pipeline, ImportFiles files, RequestContext<CallToolRequestParams> context,
        [Description(PlacementCreateTools.ShowPageDescription)] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
        => PlacementCreateTools.RunAsync(bridge, pipeline, files, context, show_page, progress, cancellationToken, annotations: true);
}
