using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// A PLC rack in one call (docs/tools/eplan_plc_rack.md): the fixed order of work an engineer follows, overviews first,
/// then the rack pictures joined to the cards (A122), so an agent supplies data instead of working out the steps.
/// Parameter names are snake_case because the SDK publishes them as written.
/// </summary>
[McpServerToolType]
internal sealed class PlcRackTools
{
    public const string Tool = "eplan_plc_rack";

    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    private static readonly JsonSerializerOptions _cards = new(HostJson.Options) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [McpServerTool(
        Name = Tool,
        Title = "Draw a PLC rack overview first",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlcRackResult))]
    [Description(
        "Draws an ET 200SP rack in the specified project in one undo step: each card's Overview on its empty Overview page, renamed with " +
        "its parts, then its picture beside the others on the empty rack page, joined to it. Omit/true previews; false " +
        "draws. Cards and an example: eplan_tool_usage.")]
    public static async Task<CallToolResult> PlcRackAsync(
        WritePipeline pipeline,
        [Description("Empty Circuit page")] string rack_page,
        [Description("1-20 cards, left to right")] JsonElement[] cards,
        [Description("Open project")] string project,
        [Description("First picture [x, y] mm")] double[]? origin = null,
        [Description("Card on overview [x, y] mm")] double[]? overview_position = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        var parsed = new List<PlcRackCard>();
        for (int i = 0; i < cards.Length; i++)
        {
            try
            {
                parsed.Add(cards[i].Deserialize<PlcRackCard>(_cards) ?? throw new JsonException("null"));
            }
            catch (JsonException)
            {
                violations.Add(string.Create(CultureInfo.InvariantCulture, $"'cards[{i}]' must be an object: {{\"tag\": ..., \"part\": ..., \"overview_page\": ..., \"picture_variant\": 5}}."));
            }
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new PlcRackArgs { Project = project, RackPage = rack_page, Cards = parsed, Origin = origin, OverviewPosition = overview_position };
        var request = new WriteRequest<PlcRackArgs, PlcRackArgs>(
            Tool,
            ToolTier.Write,
            OperationNames.PlcRackPlan,
            OperationNames.PlcRackExecute,
            args,
            plan => new PlcRackArgs
            {
                Project = plan.ProjectPath,
                RackPage = args.RackPage,
                Cards = args.Cards,
                Origin = args.Origin,
                OverviewPosition = args.OverviewPosition,
                Fingerprint = plan.Fingerprint,
            },
            dry_run,
            Limit,
            "Drawing the PLC rack");
        WriteOutcome<PlcRackData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<PlcRackArgs, PlcRackArgs, PlcRackData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        var result = new PlcRackResult
        {
            Project = outcome.Plan.Project,
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. ResultShaper.CleanChanges(outcome.Plan.Changes)],
            Cards = outcome.Data?.Cards ?? [],
            Warning = outcome.Data?.Warning,
            Undo = dry_run ? null : "One Ctrl+Z in EPLAN takes the whole rack back.",
            AuditId = outcome.AuditId,
            Recovered = outcome.Recovered,
            Hint = dry_run
                ? WriteToolDefaults.ExecutionHint
                : "Call the cards' points on the function pages with eplan_device_place (plc_connection_point); DQ above the coil, DI below the contact.",
        };
        string summary = dry_run
            ? string.Create(CultureInfo.InvariantCulture, $"Dry run: would draw {result.Changes.Count} cards on {rack_page}. Nothing was changed.")
            : string.Create(CultureInfo.InvariantCulture, $"Drew {result.Cards.Count} cards on {rack_page}, each with its overview.");
        return ToolResults.Success(summary, result);
    }
}

/// <summary>Output of <c>eplan_plc_rack</c>.</summary>
internal sealed class PlcRackResult
{
    [Description("The project.")]
    public string Project { get; set; } = string.Empty;

    [Description("not_started, committed or unknown (ADR-0010).")]
    public string Execution { get; set; } = string.Empty;

    [Description("True when this call only previewed the rack.")]
    public bool DryRun { get; set; }

    [Description("The preview's fingerprint.")]
    public string Fingerprint { get; set; } = string.Empty;

    [Description("Per card: tag, parts, overview page, picture variant, rack x and width.")]
    public List<PlannedChange> Changes { get; set; } = [];

    [Description("The drawn cards with the parts read back; empty on a dry run.")]
    public List<PlcRackCardResult> Cards { get; set; } = [];

    [Description("Layout findings that did not stop the rack.")]
    public string? Warning { get; set; }

    [Description("How to take the rack back.")]
    public string? Undo { get; set; }

    [Description("The audit record's request ID.")]
    public string? AuditId { get; set; }

    [Description("True when the result was recovered after a lost reply.")]
    public bool Recovered { get; set; }

    [Description("What to do next.")]
    public string? Hint { get; set; }
}
