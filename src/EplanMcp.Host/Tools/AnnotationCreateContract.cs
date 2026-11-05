using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

internal static class AnnotationCreateContract
{
    internal static readonly JsonElement Input = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["project","page","items"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"items":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"object","additionalProperties":false,"required":["kind"],"properties":{"kind":{"enum":["hyperlink","qr_code","spline","sector","dimension","placeholder","macro_box"]},"position":{},"points":{},"center":{},"first":{},"second":{},"end":{},"contents":{},"radius_mm":{},"start_angle":{},"end_angle":{},"link":{},"size_mm":{},"text":{},"dimension_type":{"enum":["linear","angular","baseline","incremental","continued","circle"]},"arc":{},"name":{},"records":{},"variables":{},"macro_name":{},"pen":{},"layer":{}}}},"layout_guard":{"enum":["refuse","warn"],"default":"refuse"},"snap_to_grid":{"type":"boolean","default":true},"dry_run":{"type":"boolean","default":true},"show_page":{"type":"boolean"},"response_format":{"enum":["concise","detailed"],"default":"concise"}}}
        """).RootElement.Clone();

    internal static Tool Definition => new()
    {
        Name = AnnotationCreateTools.Tool,
        Title = "Create page annotations",
        Description = "Creates hyperlinks, QR codes, splines, sectors, dimensions, placeholders and macro boxes on a Circuit, Overview or Graphics page. Coordinates: mm, bottom left, y up; angles: degrees. Omit dry_run to preview; false executes. One undo; approval outside scratch. layout_guard defaults to refuse; use warn for reproduction. Dimension values are calculated by EPLAN: linear/angular use first, second, end; baseline/incremental/continued use points and end; circle uses an existing full-circle arc locator and end. Use eplan_tool_usage for item fields. Example: {\"project\":\"P\",\"page\":\"1\",\"items\":[{\"kind\":\"spline\",\"points\":[[10,10],[20,20]]}]}",
        InputSchema = Input,
        OutputSchema = Output(),
        Annotations = new() { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false }
    };

    internal static void Configure(IEnumerable<ModelContextProtocol.Server.McpServerTool> tools)
    {
        Tool tool = tools.Single(t => t.ProtocolTool.Name == AnnotationCreateTools.Tool).ProtocolTool;
        Tool definition = Definition;
        tool.Title = definition.Title;
        tool.Description = definition.Description;
        tool.InputSchema = Input;
        tool.OutputSchema = definition.OutputSchema;
    }

    private static JsonElement Output()
    {
        JsonNode output = JsonNode.Parse(PlacementCreateContract.Output.GetRawText())!;
        JsonNode item = JsonNode.Parse(Input.GetRawText())!["properties"]!["items"]!["items"]!.DeepClone();
        item["properties"]!["dimension_text"] = JsonNode.Parse("""{"type":"array","items":{"type":"string"}}""");
        output["$defs"] = new JsonObject { ["item"] = item };
        output["properties"]!["gridSize"] = JsonNode.Parse("""{"type":["number","null"]}""");
        return JsonSerializer.SerializeToElement(output);
    }
}
