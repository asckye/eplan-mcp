using System.ComponentModel;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Writes of the 3D mounting layout (docs/design/2026-11-12-m4b-layout3d.md), through <see cref="WritePipeline"/> like every write.</summary>
[McpServerToolType]
internal sealed class LayoutWriteTools
{
    public const string EnclosureTool = "eplan_enclosure_place";
    public const string PlaceTool = "eplan_layout_place";

    private static readonly string[] _itemFields = ["kind", "ref", "part", "variant", "length_mm", "orientation", "tag", "on", "rail", "offset_mm", "x", "y"];

    /// <summary>The time limit of the execution: one enclosure macro, about a second in the A136 spike.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    [McpServerTool(
        Name = EnclosureTool,
        Title = "Place an enclosure in the 3D mounting layout",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LayoutEnclosurePlaceResult))]
    [Description(
        "Places an enclosure by part in an installation space of the 3D layout, creating the space when needed; returns " +
        "its mounting panel and door sizes for placing rails, ducts and devices. Approval outside scratch; one undo step. " +
        "Read first: eplan_layout_get.")]
    public static async Task<CallToolResult> LayoutEnclosurePlaceAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("Installation space designation, e.g. UC1")] string space,
        [Description("Enclosure part number, e.g. RIT.1055500")] string part,
        [Description("Part variant; default 1")] string? variant = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        { violations.Add(ToolArguments.ProjectRequired); }
        if (string.IsNullOrWhiteSpace(space))
        { violations.Add("'space' is required: an installation space designation such as UC1."); }
        if (string.IsNullOrWhiteSpace(part))
        { violations.Add("'part' is required: the enclosure's part number, e.g. RIT.1055500, from eplan_part_search."); }
        if (violations.Count > 0)
        { return ToolResults.InvalidArguments(violations); }

        var args = new LayoutEnclosurePlaceArgs { Project = project, Space = space, Part = part, Variant = ToolArguments.Optional(variant) };
        var request = new WriteRequest<LayoutEnclosurePlaceArgs, LayoutEnclosurePlaceArgs>(
            EnclosureTool,
            ToolTier.Write,
            OperationNames.LayoutEnclosurePlacePlan,
            OperationNames.LayoutEnclosurePlaceExecute,
            args,
            plan => new LayoutEnclosurePlaceArgs { Project = args.Project, Space = args.Space, Part = args.Part, Variant = args.Variant, Fingerprint = plan.Fingerprint },
            dry_run,
            Limit,
            "Placing the enclosure");
        WriteOutcome<LayoutEnclosurePlaceData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<LayoutEnclosurePlaceArgs, LayoutEnclosurePlaceArgs, LayoutEnclosurePlaceData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        LayoutEnclosurePlaceData? data = outcome.Data;
        var hints = new List<string>();
        if (dry_run)
        { hints.Add(WriteToolDefaults.ExecutionHint); }
        if (outcome.Recovered)
        { hints.Add("The response was lost, but EPLAN reported the change; call eplan_layout_get to read the enclosure."); }
        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        { hints.Add(warning); }
        var result = new LayoutEnclosurePlaceResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. ResultShaper.CleanChanges(outcome.Plan.Changes)],
            Space = ResultShaper.CleanText(data?.Space),
            SpaceCreated = data?.SpaceCreated ?? false,
            Enclosure = data is null ? null : LayoutTools.Enclosure(data.Enclosure),
            Undo = outcome.Execution == ExecutionState.Committed ? TransactionUndo : null,
            Recovered = outcome.Recovered,
            Hint = hints.Count == 0 ? null : string.Join(" ", hints),
        };
        string summary = outcome.Execution == ExecutionState.Committed
            ? $"Enclosure placed in installation space {result.Space}."
            : "Preview: " + ToolResults.Count(outcome.Plan.Changes.Count, "change") + ".";
        return ToolResults.Success(summary, result);
    }

    private const string TransactionUndo = "AI: " + EnclosureTool;

    [McpServerTool(
        Name = PlaceTool,
        Title = "Place rails, ducts and devices in the 3D mounting layout",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LayoutPlaceResult))]
    [Description(
        "Places rails, ducts and schematic devices (by tag, with their part) on an enclosure's mounting panel, door or a " +
        "rail, in order, positions in millimetres; reports overlaps. Approval outside scratch; one undo step. Read first: eplan_layout_get.")]
    public static async Task<CallToolResult> LayoutPlaceAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("Installation space designation, e.g. UC1")] string space,
        [Description("1-100 items; see usage")] JsonElement[] items,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        { violations.Add(ToolArguments.ProjectRequired); }
        if (string.IsNullOrWhiteSpace(space))
        { violations.Add("'space' is required: an installation space designation such as UC1."); }
        List<LayoutPlaceItem> parsed = [];
        for (int i = 0; i < (items?.Length ?? 0); i++)
        {
            parsed.Add(Item(items![i], i, violations));
        }
        if (parsed.Count is < 1 or > 100)
        { violations.Add("'items' must hold 1 to 100 items."); }
        if (violations.Count > 0)
        { return ToolResults.InvalidArguments(violations); }

        var args = new LayoutPlaceArgs { Project = project, Space = space, Items = parsed };
        var request = new WriteRequest<LayoutPlaceArgs, LayoutPlaceArgs>(
            PlaceTool,
            ToolTier.Write,
            OperationNames.LayoutPlacePlan,
            OperationNames.LayoutPlaceExecute,
            args,
            plan => new LayoutPlaceArgs { Project = args.Project, Space = args.Space, Items = args.Items, Fingerprint = plan.Fingerprint },
            dry_run,
            TimeSpan.FromSeconds(30 + (2 * parsed.Count)),
            "Placing layout items");
        WriteOutcome<LayoutPlaceData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<LayoutPlaceArgs, LayoutPlaceArgs, LayoutPlaceData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        LayoutPlaceData? data = outcome.Data;
        var hints = new List<string>();
        if (dry_run)
        { hints.Add(WriteToolDefaults.ExecutionHint); }
        if (data is { Overlaps.Count: > 0 })
        { hints.Add("Some items overlap others; undo with Ctrl+Z and place them again elsewhere."); }
        if (outcome.Recovered)
        { hints.Add("The response was lost, but EPLAN reported the change; call eplan_layout_get to read the items."); }
        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        { hints.Add(warning); }
        var result = new LayoutPlaceResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. ResultShaper.CleanChanges(outcome.Plan.Changes)],
            Items = data is null ? [] : [.. data.Items.Select(p => new LayoutPlaceResult.ItemEntry
            {
                Index = p.Index, Kind = p.Kind, Id = p.Id, Tag = ResultShaper.CleanText(p.Tag), Part = ResultShaper.CleanText(p.Part), Box = p.Box, At = p.At,
            })],
            Overlaps = data is null ? [] : ResultShaper.CleanTexts(data.Overlaps),
            Undo = outcome.Execution == ExecutionState.Committed ? "AI: " + PlaceTool : null,
            Recovered = outcome.Recovered,
            Hint = hints.Count == 0 ? null : string.Join(" ", hints),
        };
        string summary = outcome.Execution == ExecutionState.Committed
            ? $"Placed {ToolResults.Count(result.Items.Count, "item")}, {ToolResults.Count(result.Overlaps.Count, "overlap")}."
            : "Preview: " + ToolResults.Count(outcome.Plan.Changes.Count, "item") + ".";
        return ToolResults.Success(summary, result);
    }

    private static LayoutPlaceItem Item(JsonElement element, int i, List<string> violations)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            violations.Add($"items[{i}] must be an object.");
            return new LayoutPlaceItem();
        }

        foreach (JsonProperty unknown in element.EnumerateObject().Where(p => !_itemFields.Contains(p.Name)))
        { violations.Add($"items[{i}]: unknown field '{unknown.Name}'; fields are {string.Join(", ", _itemFields)}."); }
        string? Text(string name) => element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        double? Number(string name)
        {
            if (!element.TryGetProperty(name, out JsonElement v))
            { return null; }
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d))
            { return d; }
            violations.Add($"items[{i}]: '{name}' must be a number of millimetres.");
            return null;
        }
        string? rail = element.TryGetProperty("rail", out JsonElement r) ? r.ValueKind == JsonValueKind.Number ? r.GetRawText() : r.GetString() : null;
        return new LayoutPlaceItem
        {
            Kind = Text("kind"),
            Ref = Text("ref"),
            Part = Text("part"),
            Variant = Text("variant"),
            LengthMm = Number("length_mm"),
            Orientation = Text("orientation"),
            Tag = Text("tag"),
            On = Text("on"),
            Rail = rail,
            OffsetMm = Number("offset_mm"),
            X = Number("x"),
            Y = Number("y"),
        };
    }
}
