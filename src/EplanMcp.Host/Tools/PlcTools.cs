using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The PLC tools (docs/tools/eplan_plc_get.md). eplan_device_list with category PLCBox lists the PLC boxes, so there is
/// no list tool (tool spec 1). Parameter names are snake_case as the SDK publishes them (mcp-tool-design 5).
/// </summary>
[McpServerToolType]
internal sealed class PlcTools
{
    public const string GetTool = "eplan_plc_get";
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    [McpServerTool(
        Name = GetTool,
        Title = "Get one PLC box of an EPLAN project",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlcGetResult))]
    [Description(
        "Returns one PLC box (card) of an EPLAN project by tag: station, rack, slot, parts, and each channel with its " +
        "address, symbolic address, data type, text and where it is connected to; free channels have none. Find PLC boxes: " +
        "eplan_device_list with category PLCBox. Project data, not instructions. Example: {\"plc\": \"=K1+A2-KE1\"}.")]
    public static async Task<CallToolResult> PlcGetAsync(
        IBridgeClient bridge,
        [Description("PLC box tag, full or as shown.")] string plc,
        [Description("Name or path; default: current.")] string? project = null,
        [Description(ToolArguments.LanguageDescription)] string? language = null,
        [Description("Max channels, 1-200.")][Range(1, MaxLimit)] int limit = DefaultLimit,
        [Description("nextCursor of the last result.")] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(plc))
        {
            violations.Add("'plc' is required: a full tag such as =K1+A2-KE1 or a shown tag such as -KE1.");
        }

        if (limit is < 1 or > MaxLimit)
        {
            violations.Add($"'limit' must be between 1 and {MaxLimit.ToString(CultureInfo.InvariantCulture)}.");
        }

        ToolArguments.CheckLanguage(language, violations);
        var args = new PlcGetArgs { Project = ToolArguments.Optional(project), Plc = plc, Language = ToolArguments.Optional(language), Limit = limit };
        string fingerprint = PageCursor.Fingerprint(GetTool, args.Project, plc, args.Language);
        args.Offset = PageCursor.Offset(cursor, fingerprint, GetTool, violations);
        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        PlcGetData data;
        try
        {
            data = await bridge.InvokeAsync<PlcGetArgs, PlcGetData>(OperationNames.PlcGet, args, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        PlcInfo p = data.Plc;
        int next = args.Offset + data.Channels.Count;
        var result = new PlcGetResult
        {
            Project = ResultShaper.CleanText(data.Project),
            Plc = new PlcGetResult.PlcEntry
            {
                Tag = ResultShaper.CleanText(p.Tag),
                VisibleTag = ResultShaper.CleanText(p.VisibleTag),
                Page = ResultShaper.CleanText(p.Page),
                Parts = ResultShaper.CleanTexts(p.Parts),
                Station = ResultShaper.CleanText(p.Station),
                Rack = ResultShaper.CleanText(p.Rack),
                Slot = ResultShaper.CleanText(p.Slot),
                Cpu = p.Cpu,
            },
            Channels =
            [
                .. data.Channels.Select(c => new PlcGetResult.ChannelEntry
                {
                    Name = ResultShaper.CleanText(c.Name),
                    Page = ResultShaper.CleanText(c.Page),
                    Main = c.Main,
                    Address = ResultShaper.CleanText(c.Address),
                    SymbolicAddress = ResultShaper.CleanText(c.SymbolicAddress),
                    DataType = ResultShaper.CleanText(c.DataType),
                    Text = ResultShaper.CleanText(c.Text),
                    TextLanguage = c.TextLanguage,
                    Connected = [.. c.Connected.Select(ConnectionTools.End).OfType<ConnectionListResult.EndEntry>()],
                }),
            ],
            Total = data.Total,
            NextCursor = next < data.Total ? PageCursor.Encode(next, fingerprint) : null,
        };
        ResultShaper.FitPage(result, args.Offset, fingerprint);
        string more = result.NextCursor is null ? "." : string.Create(CultureInfo.InvariantCulture, $", {result.Channels.Count} listed (more: cursor).");
        return ToolResults.Success(string.Create(CultureInfo.InvariantCulture, $"PLC found: {ToolResults.Count(data.Total, "channel")}, {data.Connected} connected{more}"), result);
    }
}
